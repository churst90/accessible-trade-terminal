using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cody, 2026-11-13: "whatever pattern is selected, it must visually on screen on the chart be
/// correct as well as correct through speech". The drawing ranked formations by size alone, so a
/// semicolon choice was read aloud while another shape was the one fully labelled on screen.
/// The frame now hands the layer the speech's own order — the formation a landing at the cursor
/// names (the choice when it is there), then the rest at the cursor — and the choice's label says
/// "chosen" wherever it is drawn.
/// </summary>
public sealed class ChartFormationChoiceDrawingTests
{
    private static readonly DateTime T0 = new(2026, 11, 6, 23, 0, 0, DateTimeKind.Utc);

    // Cody's TAO/USDT 1h chart in small, as in ChartPatternPinNarrationTests.TaoChart, with real
    // times so the identities differ by time as on a real chart.
    private static readonly ChartPattern Ihs = new(
        ChartPatternKind.InverseHeadAndShoulders, ChartPatternState.Forming,
        0, 76, 80, 110, T0, T0.AddHours(76), ExpiresAtIndex: 300);
    private static readonly ChartPattern Top = new(
        ChartPatternKind.DoubleTop, ChartPatternState.Completed,
        8, 68, 72, 95, T0.AddHours(8), T0.AddHours(68),
        CompletedAtIndex: 100, ExpiresAtIndex: 300, BreaksBelow: true);
    private static readonly ChartPattern Wedge = new(
        ChartPatternKind.FallingWedge, ChartPatternState.Forming,
        80, 115, 120, 105, T0.AddHours(80), T0.AddHours(115), ExpiresAtIndex: 300);
    private static readonly ChartPattern[] All = { Ihs, Top, Wedge };

    private static WorkspaceState At(int index) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 130)
            .Select(i => new Ohlcv(T0.AddHours(i), 100, 101, 99, 100, 10)).ToArray()),
        CurrentDataIndex = index,
    };

    private static string Key => ChartPatternCache.KeyFor(WorkspaceState.Initial.Identity);

    private static ChartPatternFocus ChooseTheDoubleTop()
    {
        var focus = new ChartPatternFocus();
        // At bar 80 the head and shoulders leads and the double top is second: one press.
        var ranked = ChartPatternNarrator.ByDominance(ChartPatternNarrator.AtBar(All, 80)).ToList();
        Assert.Equal(ChartPatternKind.DoubleTop, focus.CycleAt(Key, ranked)?.Kind);
        return focus;
    }

    /// <summary>
    /// On a bar of the chosen formation that is no one's start or end (85), the speech names the
    /// choice, so it is drawn first.
    /// </summary>
    [Fact]
    public void TheChosenFormationLeadsTheDrawingOnItsOwnBars()
    {
        var (lead, chosen) = new ChartFrameRenderer(null, patternFocus: ChooseTheDoubleTop()).Lead(At(85), All);

        Assert.Equal(Top.Identity, chosen);
        var drawn = ChartFormationLayer.Ordered(All, lead).ToList();
        Assert.Equal(new[] { Top, Ihs, Wedge }, drawn);   // by size alone the head and shoulders was first
    }

    /// <summary>
    /// At End the double top is over and the speech no longer names it (Cody, 2026-11-13: "I don't
    /// want to hear about another pattern that isn't the pattern start/end I landed on"), so it
    /// does not lead the drawing either; it is drawn after what is at the cursor.
    /// </summary>
    [Fact]
    public void AChoiceElsewhereDoesNotLeadTheDrawing()
    {
        var (lead, chosen) = new ChartFrameRenderer(null, patternFocus: ChooseTheDoubleTop()).Lead(At(129), All);

        Assert.Equal(Top.Identity, chosen);
        Assert.Equal(new[] { Ihs.Identity, Wedge.Identity }, lead);
        Assert.Equal(new[] { Ihs, Wedge, Top }, ChartFormationLayer.Ordered(All, lead).ToList());
    }

    /// <summary>
    /// On a formation's first bar the speech names that formation, so the drawing leads with it,
    /// though a bigger one is at the cursor too — and though the choice is in play there (80).
    /// </summary>
    [Fact]
    public void OnAFormationsStartTheDrawingLeadsWithIt()
    {
        var frame = new ChartFrameRenderer(null, patternFocus: ChooseTheDoubleTop());

        Assert.Equal(new[] { Wedge.Identity, Ihs.Identity }, frame.Lead(At(120), All).Lead);
        Assert.Equal(new[] { Ihs.Identity, Top.Identity }, frame.Lead(At(80), All).Lead);
    }

    /// <summary>With nothing chosen the drawing leads with what the readout leads with at the cursor.</summary>
    [Fact]
    public void WithNoChoiceTheDrawingFollowsTheReadoutAtTheCursor()
    {
        var (lead, chosen) = new ChartFrameRenderer(null, patternFocus: new ChartPatternFocus()).Lead(At(129), All);

        Assert.Null(chosen);
        Assert.Equal(new[] { Ihs.Identity, Wedge.Identity }, lead);
        // The double top is not at the cursor, so it follows by size.
        Assert.Equal(new[] { Ihs, Wedge, Top }, ChartFormationLayer.Ordered(All, lead).ToList());
    }

    /// <summary>
    /// The drawing reads the choice from the same focus the speech does. Both heads build the
    /// frame renderer by dependency injection; were the focus left out there, the drawing would
    /// silently ignore every choice while every test above passed.
    /// </summary>
    [Fact]
    public void DependencyInjectionHandsTheFrameRendererTheSpeechsFocus()
    {
        var sp = new ServiceCollection()
            .AddSingleton<ChartRenderer>(_ => null!)
            .AddSingleton<IChartPatternFocus>(ChooseTheDoubleTop())
            .AddSingleton<ChartFrameRenderer>()
            .BuildServiceProvider();

        var (_, chosen) = sp.GetRequiredService<ChartFrameRenderer>().Lead(At(129), All);
        Assert.Equal(Top.Identity, chosen);
    }

    private sealed class FixedPatterns : IChartPatternCache
    {
        public IReadOnlyList<ChartPattern> For(ChartIdentity identity, IReadOnlyList<Ohlcv>? bars) => All;
    }

    /// <summary>A real frame: the choice reaches the chart renderer that hands it to the layer.</summary>
    [Fact]
    public void ARealFrameDrawsTheChosenFormationFirstAndLabelsIt()
    {
        var themeSettings = Substitute.For<ISettingsManager>();
        themeSettings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var renderer = new ChartRenderer(new ThemeService(themeSettings),
            new StylingService(new ComponentRoleMapper(),
                new Core.Services.Audio.SonificationProfileProvider(), new PaneAssignmentService()),
            new PaneLayoutService(), NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var app = Substitute.For<IAppSettings>();
        app.ShowChartPatternVisuals.Returns(true);
        app.UiScale.Returns(100);

        var state = At(85) with
        {
            ViewportStartIndex = 0, ViewportLength = 130, RightMarginBars = 0, ViewportRange = (90, 120),
            PaneRanges = System.Collections.Immutable.ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (90, 120)),
        };
        using var bmp = new SKBitmap(800, 600);
        using (var canvas = new SKCanvas(bmp))
            new ChartFrameRenderer(renderer, new FixedPatterns(), app, ChooseTheDoubleTop()).Render(canvas, 800, 600, state, 1f);

        // At 85 the readout names the double top, so it is drawn first and labelled "chosen";
        // by size alone the head and shoulders came first.
        var drawn = renderer.FormationLayer.LastDrawn;
        Assert.Equal(new[] { (Top, true), (Ihs, false), (Wedge, false) }, drawn);
    }

    [Fact]
    public void WithoutALeadTheDrawingIsBySizeAsBefore()
        => Assert.Equal(ChartPatternNarrator.ByDominance(All), ChartFormationLayer.Ordered(All, null));

    [Fact]
    public void TheChosenFormationsLabelSaysChosen()
    {
        Assert.StartsWith("chosen: double top neckline", ChartFormationLayer.TriggerLabel(Top, chosen: true));
        Assert.Equal(ChartFormationLayer.TriggerLabel(Top), ChartFormationLayer.TriggerLabel(Top, chosen: false));
    }
}
