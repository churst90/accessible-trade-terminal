using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Ctrl+Left and Ctrl+Right jump to something worth hearing — including on an indicator whose
/// lines are bands rather than extremes.</b>
///
/// <para>
/// Cody, 2026-09-12: <i>"when I add adx, ctrl left/right seems to move along every point on each
/// component, not just notable crosses into different zones even on the adx component."</i> Two
/// separate causes, both here.
/// </para>
///
/// <para>
/// <b>It walked bar by bar</b> because the "is this a sparse marker" test was "does the array
/// contain any NaN". Nearly every indicator has a warmup — ADX's first fourteen bars are NaN
/// because a fourteen-period average needs fourteen bars — so ADX read as a sparse marker, fell to
/// the marker jump, and that jump lands on the next bar with a value: every bar, one at a time, on
/// a key whose entire purpose is to skip.
/// </para>
///
/// <para>
/// <b>And it had nothing to aim at</b> because the threshold jump read exactly three lines — an
/// overbought, an oversold and the midline between them — and REQUIRED the first two. ADX's
/// Developing / Strong / Very Strong are band edges, so the classifier fell past every branch to
/// "no dedicated rule". Since those lines declare what their two sides mean, they are destinations.
/// </para>
/// </summary>
public sealed class BandCrossingNavigationTests
{
    // ── The sparse test, which is where the bar-by-bar walk came from ────────

    /// <summary>A warmup prefix is not sparseness. This is ADX, and every other indicator.</summary>
    [Fact]
    public void AContinuousLineWithAWarmupPrefix_IsNotASparseSignal()
    {
        var adx = Enumerable.Range(0, 200)
            .Select(i => i < 14 ? double.NaN : 20.0 + 10 * Math.Sin(i / 20.0)).ToArray();
        Assert.False(IndicatorCrossingEngine.IsSparseSignal(adx));
    }

    /// <summary>A marker that fires a handful of times is.</summary>
    [Fact]
    public void AMarkerThatFiresOnAFewBars_IsASparseSignal()
    {
        var dots = Enumerable.Range(0, 200).Select(i => i % 40 == 0 ? 1.0 : double.NaN).ToArray();
        Assert.True(IndicatorCrossingEngine.IsSparseSignal(dots));
    }

    /// <summary>
    /// A feed with a few missing bars in the middle is still a line. An "any interior gap" rule
    /// would call it sparse and put the key back to walking bar by bar.
    /// </summary>
    [Fact]
    public void ALineWithAFewMissingBars_IsStillALine()
    {
        var data = Enumerable.Range(0, 200).Select(i => i is 57 or 58 or 101 ? double.NaN : 42.0).ToArray();
        Assert.False(IndicatorCrossingEngine.IsSparseSignal(data));
    }

    [Fact]
    public void AnEmptyOrAllNaNComponent_IsNotAMarker()
    {
        Assert.False(IndicatorCrossingEngine.IsSparseSignal(Array.Empty<double>()));
        Assert.False(IndicatorCrossingEngine.IsSparseSignal(Enumerable.Repeat(double.NaN, 50).ToArray()));
        Assert.False(IndicatorCrossingEngine.IsSparseSignal(null));
    }

    /// <summary>A single firing is the sparse case by definition.</summary>
    [Fact]
    public void ASingleFiring_IsASparseSignal()
    {
        var one = Enumerable.Range(0, 100).Select(i => i == 42 ? 1.0 : double.NaN).ToArray();
        Assert.True(IndicatorCrossingEngine.IsSparseSignal(one));
    }

    // ── ADX, through the real engine ─────────────────────────────────────────

    /// <summary>ADX as production builds it: a warmup, then a line that walks up through its bands.</summary>
    private static ChartSeries Adx(double[] values)
    {
        var config = new SeriesConfig { Id = "adx", IndicatorCode = "Adx", Name = "ADX", Pane = "Pane_Adx" };
        config.Components.Add(new ComponentConfig { Name = "Adx", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        config.Levels.Add(new LevelConfig { Name = "Developing", Value = 20, IsVisible = true, AboveLabel = "developing trend", BelowLabel = "no trend" });
        config.Levels.Add(new LevelConfig { Name = "Strong", Value = 25, IsVisible = true, AboveLabel = "strong trend" });
        config.Levels.Add(new LevelConfig { Name = "Very Strong", Value = 50, IsVisible = true, AboveLabel = "very strong trend" });
        var buffer = new SeriesDataBuffer { SeriesId = "adx" };
        buffer.ComponentData["Adx"] = values;
        return new ChartSeries(config, buffer);
    }

    private static (int? Landed, string? Said) JumpRight(ChartSeries series, int from, int bars)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, bars).Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100, 101, 99, 100, 10)).ToList()),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = from,
            ViewportStartIndex = 0,
            ViewportLength = bars,
        });
        new IndicatorCrossingEngine(store, bus).HandleCrossJump(SystemCommand.NavRightJump);
        return (store.DispatchedActions.OfType<NavigateAction>().LastOrDefault()?.NewIndex,
                bus.Log.OfType<FeedbackRequestEvent>().LastOrDefault()?.Message);
    }

    /// <summary>
    /// Cody's report as a number. A ramp from 10 to 60 crosses 20, 25 and 50 once each; the jump
    /// from bar 20 must land on the 25 crossing, not on bar 21.
    /// </summary>
    [Fact]
    public void OnAdx_TheJumpSkipsToTheNextBandCrossing_NotTheNextBar()
    {
        const int bars = 120;
        // Warmup, then a straight ramp 10 → 60 over the remaining bars.
        var values = Enumerable.Range(0, bars)
            .Select(i => i < 14 ? double.NaN : 10.0 + 50.0 * (i - 14) / (bars - 15)).ToArray();
        var series = Adx(values);

        int firstAbove25 = Array.FindIndex(values, v => !double.IsNaN(v) && v >= 25);
        var result = JumpRight(series, from: firstAbove25 - 5, bars: bars);

        Assert.NotNull(result.Landed);
        Assert.Equal(firstAbove25, result.Landed);
        Assert.True(result.Landed > firstAbove25 - 5 + 1,
            "the jump landed on the very next bar — it is still walking rather than skipping");
    }

    /// <summary>And it names the zone it arrived in, which is what the band declaration is for.</summary>
    [Fact]
    public void OnAdx_TheJumpSaysWhichBandItLandedIn()
    {
        const int bars = 120;
        var values = Enumerable.Range(0, bars)
            .Select(i => i < 14 ? double.NaN : 10.0 + 50.0 * (i - 14) / (bars - 15)).ToArray();

        var result = JumpRight(Adx(values), from: 20, bars: bars);

        Assert.NotNull(result.Said);
        Assert.Contains("trend", result.Said!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No points of interest", result.Said!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walking right repeatedly visits each band edge once and then stops, rather than stepping
    /// through every bar. Three crossings on this ramp, then nothing.
    /// </summary>
    [Fact]
    public void OnAdx_WalkingRightVisitsEachBandOnce()
    {
        const int bars = 120;
        var values = Enumerable.Range(0, bars)
            .Select(i => i < 14 ? double.NaN : 10.0 + 50.0 * (i - 14) / (bars - 15)).ToArray();
        var series = Adx(values);

        var stops = new List<int>();
        int at = 14;
        for (int i = 0; i < 6; i++)
        {
            var r = JumpRight(series, from: at, bars: bars);
            if (r.Landed == null) break;
            stops.Add(r.Landed.Value);
            at = r.Landed.Value;
        }

        Assert.Equal(3, stops.Count);
        Assert.Equal(stops.Distinct().Count(), stops.Count);
        Assert.True(stops.Zip(stops.Skip(1)).All(p => p.Second - p.First > 1),
            $"consecutive stops must be more than one bar apart: {string.Join(", ", stops)}");
    }

    /// <summary>
    /// An unlabelled line of role None is still not a destination. Jumping to a line the key
    /// cannot describe on arrival would be the bar-by-bar walk wearing a better name.
    /// </summary>
    [Fact]
    public void AnUnlabelledDecorativeLine_IsNotADestination()
    {
        const int bars = 60;
        var values = Enumerable.Range(0, bars).Select(i => 10.0 + i).ToArray();
        var series = Adx(values);
        foreach (var l in series.Config.Levels) { l.AboveLabel = null; l.BelowLabel = null; }

        var result = JumpRight(series, from: 5, bars: bars);

        Assert.Null(result.Landed);
        Assert.Contains("No points of interest", result.Said ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
