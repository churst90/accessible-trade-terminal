using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Core.Services.Drawing.Calculators;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A fib's levels, and the other lines a drawing draws, chirp when price crosses them and are
/// Ctrl+Left/Right stops.</b>
///
/// <para>
/// Cody, 2026-11-01: <i>"yes i want the fib levels narrated."</i> Only horizontal and vertical
/// lines chirped; arrowing through the 61.8% level was silent, and Ctrl+Right on a focused fib
/// said "No points of interest". The rule is the horizontal line's — the bar landed on and the
/// bar before straddle the line — against the line's value on each bar, so a sloped channel or
/// neckline is crossed where it is drawn. A fib is crossed only after its last point.
/// </para>
/// </summary>
public sealed class DrawingLevelCrossingTests
{
    private static readonly DateTime T0 = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TimeSeriesBuffer<Ohlcv> Closes(params double[] closes) =>
        new(closes.Select((c, i) => new Ohlcv(T0.AddDays(i), c, c, c, c, 1000)).ToList());

    private static ChartSeries Candles()
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", FriendlyName = "Candles", Pane = "Main" };
        config.Components.Add(new ComponentConfig { Name = "body", DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, IsVisible = true });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    /// <summary>A drawing with the lines its calculator returns, as the orchestrator stores them.</summary>
    private static ChartSeries Drawn(DrawingData d, Dictionary<string, double[]> lines, string id, string name)
    {
        var config = new SeriesConfig { Id = id, Name = d.Type.ToString(), FriendlyName = name, Pane = "Main" };
        var buffer = new SeriesDataBuffer { SeriesId = id };
        foreach (var (key, values) in lines)
        {
            config.Components.Add(new ComponentConfig { Name = key, DisplayType = ComponentDisplayType.Line, IsVisible = true });
            buffer.ComponentData[key] = values;
        }
        return new ChartSeries(config, buffer) { Drawing = d };
    }

    /// <summary>A retracement drawn from the low at bar 0 (100) to the high at bar 2 (200):
    /// 23.6% at 176.4, 38.2% at 161.8, 50% at 150, 61.8% at 138.2.</summary>
    private static ChartSeries Fib(int count, string id = "fib")
    {
        var d = new DrawingData
        {
            Type = DrawingType.FibRetracement,
            AnchorDate1 = T0, AnchorPrice1 = 100,
            AnchorDate2 = T0.AddDays(2), AnchorPrice2 = 200,
        };
        return Drawn(d, FibLevels.Lines(d, count), id, "Fib retracement");
    }

    //                                0    1    2    3    4    5    6
    private static readonly double[] Swing = { 100, 150, 200, 190, 170, 130, 180 };

    private static (IndicatorCrossingEngine Engine, WorkspaceStore Store, List<FeedbackRequestEvent> Spoken) Engine(
        TimeSeriesBuffer<Ohlcv> data, int cursor, string focusedId, params ChartSeries[] series)
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(st => st with
        {
            Data = data,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = focusedId,
            FocusedComponentIndex = 0,
            CurrentDataIndex = cursor,
        }));
        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);
        return (new IndicatorCrossingEngine(store, bus), store, spoken);
    }

    // ── Ctrl+Left/Right ──────────────────────────────────────────────────────

    [Fact]
    public void OnAFocusedFib_CtrlRight_WalksPriceAcrossItsLevels_NamingThem()
    {
        var data = Closes(Swing);
        var (engine, store, spoken) = Engine(data, 3, "fib", Candles(), Fib(data.Count));

        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(4, store.State.CurrentDataIndex);
        Assert.Contains("below 23.6% level at 176.4", spoken[^1].Message);

        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(5, store.State.CurrentDataIndex);
        // One bar through three levels: all three named.
        Assert.Contains("below 38.2% level", spoken[^1].Message);
        Assert.Contains("below 50% level", spoken[^1].Message);
        Assert.Contains("below 61.8% level at 138.2", spoken[^1].Message);

        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(6, store.State.CurrentDataIndex);
        Assert.Contains("above 61.8% level", spoken[^1].Message);
    }

    /// <summary>Bar 1 rises through the 50% level, but that is the swing being made, before the
    /// fib's last point: no stop.</summary>
    [Fact]
    public void AFibIsNotCrossedBeforeItsLastPoint()
    {
        var data = Closes(Swing);
        var (engine, store, spoken) = Engine(data, 3, "fib", Candles(), Fib(data.Count));

        engine.HandleCrossJump(SystemCommand.NavLeftJump);

        Assert.Equal(3, store.State.CurrentDataIndex);
        Assert.Equal(FeedbackType.Boundary, spoken[^1].Type);
        Assert.Contains("No crossing", spoken[^1].Message);
    }

    [Fact]
    public void OnTheCandles_CtrlRight_StopsAtFibLevels_AlongsideOtherLines()
    {
        var data = Closes(Swing);
        var (engine, store, spoken) = Engine(data, 3, CoreSeriesIds.Candles, Candles(), Fib(data.Count));

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(4, store.State.CurrentDataIndex);
        Assert.Contains("23.6% level", spoken[^1].Message);
    }

    [Fact]
    public void AFibLevelSwitchedOff_IsNotAStop()
    {
        var data = Closes(Swing);
        var fib = Fib(data.Count);
        fib.Components.First(c => c.Name == FibLevels.Key(0.236, DrawingType.FibRetracement)).IsVisible = false;
        var (engine, store, _) = Engine(data, 3, "fib", Candles(), fib);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(5, store.State.CurrentDataIndex);
    }

    /// <summary>A sloped line is crossed against its value on each bar: a channel's lower bound
    /// rising 10 a bar from 100 is crossed on bar 3, where the close (125) falls under it (130),
    /// though the close never moves below any one fixed price it had passed.</summary>
    [Fact]
    public void ASlopedLine_IsCrossedWhereItIsDrawn()
    {
        var data = Closes(105, 115, 125, 125, 125);
        var lower = new[] { 100.0, 110, 120, 130, 140 };
        var d = new DrawingData { Type = DrawingType.Channel, AnchorDate1 = T0, AnchorPrice1 = 100, AnchorDate2 = T0.AddDays(1), AnchorPrice2 = 110 };
        var channel = Drawn(d, new() { ["Lower Bound"] = lower }, "ch", "Channel");
        var (engine, store, spoken) = Engine(data, 0, "ch", Candles(), channel);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(3, store.State.CurrentDataIndex);
        Assert.Contains("below channel lower bound at 130", spoken[^1].Message);
    }

    /// <summary>Of a head and shoulders, only the neckline is a line price crosses; the legs
    /// joining the five points are not.</summary>
    [Fact]
    public void APatternsLegs_AreNotCrossed_OnlyItsNeckline()
    {
        var d = new DrawingData { Type = DrawingType.HeadAndShoulders, AnchorDate1 = T0, AnchorPrice1 = 1 };
        var hs = Drawn(d, new() { ["Leg 1"] = new double[3], [HeadAndShouldersCalculator.Neckline] = new double[3] }, "hs", "Head and shoulders");

        var names = LineDrawingCrossings.PriceLines(hs, Closes(1, 2, 3)).Select(l => l.Name).ToList();

        Assert.Equal(new[] { "neckline" }, names);
    }

    // ── The crossing earcon ──────────────────────────────────────────────────

    private static (LevelCrossingMonitor Monitor, IAudioDriver Driver) Monitor()
    {
        var driver = Substitute.For<IAudioDriver>();
        var sonifier = new NavigationSonifier(driver, new DefaultSonificationStrategy(Substitute.For<ISoundPatchRegistry>()), Substitute.For<ISoundPatchRegistry>());
        return (new LevelCrossingMonitor(sonifier), driver);
    }

    private static WorkspaceState At(int index, TimeSeriesBuffer<Ohlcv> data, params ChartSeries[] series) =>
        WorkspaceState.Initial with
        {
            Data = data,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = CoreSeriesIds.Candles,
            FocusedComponentIndex = 0,
            CurrentDataIndex = index,
            ViewportStartIndex = 0,
            ViewportLength = data.Count,
            InitStatus = InitializationStatus.Ready,
        };

    private static int Chirps(IAudioDriver driver) => driver.ReceivedCalls()
        .Count(c => c.GetMethodInfo().Name == nameof(IAudioDriver.SetVoice)
                    && c.GetArguments().Length > 1
                    && c.GetArguments()[0] is int slot && slot == CrossEarcon.SlotA);

    /// <summary>Arrowing right, bar by bar: bar 3 holds at the high (200, on the 0% line, not
    /// through it) and is quiet; bars 4, 5 and 6 each cross a level.</summary>
    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void ArrowingAcrossAFibLevel_PlaysTheCrossingEarcon(int bar, bool chirps)
    {
        var data = Closes(100, 150, 200, 200, 170, 130, 180);
        var (monitor, driver) = Monitor();
        var fib = Fib(data.Count);

        monitor.OnBarNavigated(At(bar - 1, data, Candles(), fib));
        driver.ClearReceivedCalls();
        monitor.OnBarNavigated(At(bar, data, Candles(), fib));

        Assert.Equal(chirps, Chirps(driver) > 0);
    }

    [Fact]
    public void ArrowingAcrossAFibLevel_BeforeItsLastPoint_IsSilent()
    {
        var data = Closes(Swing);
        var (monitor, driver) = Monitor();
        var fib = Fib(data.Count);

        monitor.OnBarNavigated(At(0, data, Candles(), fib));
        driver.ClearReceivedCalls();
        monitor.OnBarNavigated(At(1, data, Candles(), fib));

        Assert.Equal(0, Chirps(driver));
    }

    [Fact]
    public void AMutedFib_IsSilent()
    {
        var data = Closes(Swing);
        var (monitor, driver) = Monitor();
        var fib = Fib(data.Count);
        fib.IsMuted = true;

        monitor.OnBarNavigated(At(4, data, Candles(), fib));
        driver.ClearReceivedCalls();
        monitor.OnBarNavigated(At(5, data, Candles(), fib));

        Assert.Equal(0, Chirps(driver));
    }

    /// <summary>A trend line chirps too, where Ctrl+Left/Right stops on it: the line through
    /// (0, 100) and (4, 140) is 120 at bar 2 and 130 at bar 3; the close goes 125 → 125, from
    /// above to below.</summary>
    [Fact]
    public void ArrowingAcrossATrendLine_PlaysTheCrossingEarcon()
    {
        var data = Closes(90, 100, 125, 125, 125);
        var d = new DrawingData { Type = DrawingType.TrendLine, AnchorDate1 = T0, AnchorPrice1 = 100, AnchorDate2 = T0.AddDays(4), AnchorPrice2 = 140 };
        var tl = Drawn(d, new() { ["Line"] = new double[data.Count] }, "tl", "Trend line");
        var (monitor, driver) = Monitor();

        monitor.OnBarNavigated(At(2, data, Candles(), tl));
        driver.ClearReceivedCalls();
        monitor.OnBarNavigated(At(3, data, Candles(), tl));

        Assert.True(Chirps(driver) > 0);
    }
}
