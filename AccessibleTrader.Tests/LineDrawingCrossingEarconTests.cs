using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Horizontal and vertical line drawings play the crossing earcon.</b>
///
/// <para>
/// Cody, 2026-10-09: <i>"vertical and horizontals should play the crossing earcon when price
/// crosses them."</i> They played nothing. A horizontal line is a price somebody marked, and
/// arrowing across it was silent unless that line happened to be the focused series — the crossing
/// chirp was only ever computed for the focused component against its own series' levels, and a
/// drawing has no levels.
/// </para>
///
/// <para>
/// These drive the real <see cref="LevelCrossingMonitor"/> into the real
/// <see cref="NavigationSonifier"/> and listen at the audio driver, on the two slots the crossing
/// chirp owns (<see cref="CrossEarcon.SlotA"/>). The rule is the reference level's: the bar the
/// cursor lands on and the bar before it straddle the line, up if the close rose through it and
/// down if it fell.
/// </para>
/// </summary>
public sealed class LineDrawingCrossingEarconTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TimeSeriesBuffer<Ohlcv> Closes(params double[] closes) =>
        new(closes.Select((c, i) => new Ohlcv(T0.AddMinutes(i), c, c, c, c, 1)).ToList());

    private static ChartSeries Candles()
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", FriendlyName = "Candles", Pane = "Main" };
        config.Components.Add(new ComponentConfig { Name = "body", DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, IsVisible = true });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    internal static ChartSeries Horizontal(double price, string id = "hl")
    {
        var config = new SeriesConfig { Id = id, Name = "Horizontal", FriendlyName = "Horizontal line", Pane = "Main" };
        config.Components.Add(new ComponentConfig { Name = "Line", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        var s = new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
        s.Drawing = new DrawingData { Type = DrawingType.HorizontalLine, AnchorPrice1 = price };
        return s;
    }

    internal static ChartSeries Vertical(DateTime at, string id = "vl")
    {
        var config = new SeriesConfig { Id = id, Name = "Vertical", FriendlyName = "Vertical line", Pane = "Main" };
        config.Components.Add(new ComponentConfig { Name = "Line", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        var s = new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
        s.Drawing = new DrawingData { Type = DrawingType.VerticalLine, AnchorDate1 = at };
        return s;
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

    private static (LevelCrossingMonitor Monitor, IAudioDriver Driver) Build()
    {
        var driver = Substitute.For<IAudioDriver>();
        var sonifier = new NavigationSonifier(driver, new DefaultSonificationStrategy(Substitute.For<ISoundPatchRegistry>()), Substitute.For<ISoundPatchRegistry>());
        return (new LevelCrossingMonitor(sonifier), driver);
    }

    /// <summary>The first note of every crossing chirp the driver was asked for — its pitch is the
    /// direction (low first = rising, high first = falling). The second note is scheduled ~70 ms
    /// later and is deliberately not waited for.</summary>
    private static List<double> Chirps(IAudioDriver driver) => driver.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(IAudioDriver.SetVoice)
                    && c.GetArguments().Length > 1
                    && c.GetArguments()[0] is int slot && slot == CrossEarcon.SlotA)
        .Select(c => (double)c.GetArguments()[1]!)
        .ToList();

    private const double Rising = 600; // CrossEarcon's two notes sit either side of this

    // ── Horizontal line ──────────────────────────────────────────────────────

    [Fact]
    public void PriceRisingThroughAHorizontalLine_PlaysTheRisingCrossingEarcon()
    {
        var data = Closes(90, 110);
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(1, data, Candles(), Horizontal(100)));

        var chirp = Assert.Single(Chirps(driver));
        Assert.True(chirp < Rising, "Price rose through the line, so the chirp should rise (start on the low note).");
    }

    [Fact]
    public void PriceFallingThroughAHorizontalLine_PlaysTheFallingCrossingEarcon()
    {
        var data = Closes(110, 90);
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(1, data, Candles(), Horizontal(100)));

        var chirp = Assert.Single(Chirps(driver));
        Assert.True(chirp > Rising, "Price fell through the line, so the chirp should fall (start on the high note).");
    }

    /// <summary>Arrowing BACK onto the crossing bar sounds it too — it is a fact about the bar, as
    /// it is for a reference level.</summary>
    [Fact]
    public void ArrowingBackOntoTheCrossingBar_PlaysItToo()
    {
        var data = Closes(90, 110, 120);
        var (monitor, driver) = Build();
        var hl = Horizontal(100);

        monitor.OnBarNavigated(At(2, data, Candles(), hl));
        Assert.Empty(Chirps(driver));

        monitor.OnBarNavigated(At(1, data, Candles(), hl));
        Assert.Single(Chirps(driver));
    }

    /// <summary>The negative half: bars on one side of the line make no sound.</summary>
    [Fact]
    public void BarsThatStayOnOneSide_AreSilent()
    {
        var data = Closes(90, 95, 99);
        var (monitor, driver) = Build();
        var hl = Horizontal(100);

        monitor.OnBarNavigated(At(1, data, Candles(), hl));
        monitor.OnBarNavigated(At(2, data, Candles(), hl));

        Assert.Empty(Chirps(driver));
    }

    /// <summary>The level rule's tie-break, not a new one: touching the line from below counts as
    /// reaching it (at-or-above is "above"), as it does for a reference level.</summary>
    [Fact]
    public void ClosingExactlyOnTheLineFromBelow_CountsAsACross()
    {
        var data = Closes(90, 100);
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(1, data, Candles(), Horizontal(100)));

        Assert.Single(Chirps(driver));
    }

    [Fact]
    public void AHiddenHorizontalLine_IsSilent()
    {
        var data = Closes(90, 110);
        var (monitor, driver) = Build();
        var hl = Horizontal(100);
        hl.IsVisible = false;

        monitor.OnBarNavigated(At(1, data, Candles(), hl));

        Assert.Empty(Chirps(driver));
    }

    [Fact]
    public void AMutedHorizontalLine_IsSilent()
    {
        var data = Closes(90, 110);
        var (monitor, driver) = Build();
        var hl = Horizontal(100);
        hl.IsMuted = true;

        monitor.OnBarNavigated(At(1, data, Candles(), hl));

        Assert.Empty(Chirps(driver));
    }

    /// <summary>The line is heard whichever series holds focus — like a level's approach ping, it is
    /// a fact about the chart, not about the focused component.</summary>
    [Fact]
    public void TheCrossIsHeard_WithAnIndicatorFocused()
    {
        var data = Closes(90, 110);
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(1, data, Candles(), Horizontal(100)) with { FocusedSeriesId = "rsi" });

        Assert.Single(Chirps(driver));
    }

    /// <summary>Two lines crossed on one bar are one event to the ear: the chirp owns two fixed
    /// slots, and a second call would only restart the first.</summary>
    [Fact]
    public void TwoLinesCrossedOnOneBar_PlayOneChirp()
    {
        var data = Closes(90, 110);
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(1, data, Candles(), Horizontal(100), Horizontal(105, id: "hl2")));

        Assert.Single(Chirps(driver));
    }

    // ── Vertical line ────────────────────────────────────────────────────────

    [Fact]
    public void ArrowingOntoAVerticalLinesBar_PlaysTheCrossingEarcon_InEitherDirection()
    {
        var data = Closes(100, 100, 100, 100, 100);
        var vl = Vertical(T0.AddMinutes(2));
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(1, data, Candles(), vl));
        Assert.Empty(Chirps(driver));

        monitor.OnBarNavigated(At(2, data, Candles(), vl));      // onto it, moving right
        var right = Assert.Single(Chirps(driver));
        Assert.True(right < Rising, "Moving right onto the line should chirp upward.");

        monitor.OnBarNavigated(At(3, data, Candles(), vl));      // stepping off it is not a cross
        Assert.Single(Chirps(driver));

        monitor.OnBarNavigated(At(2, data, Candles(), vl));      // onto it, moving left
        var both = Chirps(driver);
        Assert.Equal(2, both.Count);
        Assert.True(both[1] > Rising, "Moving left onto the line should chirp downward.");
    }

    /// <summary>A jump that passes OVER the line still crosses it — Home, End, Page Up/Down and the
    /// Ctrl jumps must not be able to skip it silently.</summary>
    [Fact]
    public void JumpingAcrossAVerticalLine_PlaysTheCrossingEarcon()
    {
        var data = Closes(100, 100, 100, 100, 100, 100);
        var vl = Vertical(T0.AddMinutes(2));
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(0, data, Candles(), vl));
        monitor.OnBarNavigated(At(5, data, Candles(), vl));

        Assert.Single(Chirps(driver));
    }

    [Fact]
    public void AHiddenVerticalLine_IsSilent()
    {
        var data = Closes(100, 100, 100);
        var vl = Vertical(T0.AddMinutes(2));
        vl.IsVisible = false;
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(1, data, Candles(), vl));
        monitor.OnBarNavigated(At(2, data, Candles(), vl));

        Assert.Empty(Chirps(driver));
    }

    /// <summary>A symbol or timeframe change forgets where the cursor was, so the first bar of the
    /// new chart is not read as a jump across every vertical line between two unrelated charts.</summary>
    [Fact]
    public void AfterReset_TheFirstBarIsNotAJumpFromTheOldChart()
    {
        var data = Closes(100, 100, 100, 100, 100, 100);
        var vl = Vertical(T0.AddMinutes(2));
        var (monitor, driver) = Build();

        monitor.OnBarNavigated(At(0, data, Candles(), vl));
        monitor.Reset();
        monitor.OnBarNavigated(At(5, data, Candles(), vl));

        Assert.Empty(Chirps(driver));
    }

    // ── The gates: the same ones a level's cue answers to ────────────────────

    private static (SonificationManager Manager, MockWorkspaceStore Store, IAudioDriver Driver) BuildManager()
    {
        var store = new MockWorkspaceStore();
        var driver = Substitute.For<IAudioDriver>();
        var nav = new NavigationSonifier(driver, new DefaultSonificationStrategy(Substitute.For<ISoundPatchRegistry>()), Substitute.For<ISoundPatchRegistry>());
        var mainThread = Substitute.For<IMainThreadService>();
        mainThread.When(m => m.InvokeOnMainThread(Arg.Any<Action>())).Do(ci => ci.Arg<Action>().Invoke());
        var mgr = new SonificationManager(Substitute.For<IPlaybackOrchestrator>(), nav, store, mainThread,
            new SpyEventBus(), new LevelCrossingMonitor(nav));
        return (mgr, store, driver);
    }

    /// <summary>Through the real manager, so the wiring is what is tested: arrowing across the line
    /// is heard...</summary>
    [Fact]
    public void ThroughTheSonificationManager_ArrowingAcrossTheLine_IsHeard()
    {
        var data = Closes(90, 110);
        var hl = Horizontal(100);
        var (mgr, store, driver) = BuildManager();
        using (mgr)
        {
            store.EmitState(At(0, data, Candles(), hl));
            store.EmitState(At(1, data, Candles(), hl));
        }

        Assert.Single(Chirps(driver));
    }

    /// <summary>...and with chart sound switched off (F3) it is not.</summary>
    [Fact]
    public void WithChartSoundOff_TheLineIsSilent()
    {
        var data = Closes(90, 110);
        var hl = Horizontal(100);
        var (mgr, store, driver) = BuildManager();
        using (mgr)
        {
            store.EmitState(At(0, data, Candles(), hl) with { IsSonificationEnabled = false });
            store.EmitState(At(1, data, Candles(), hl) with { IsSonificationEnabled = false });
        }

        Assert.Empty(Chirps(driver));
    }
}
