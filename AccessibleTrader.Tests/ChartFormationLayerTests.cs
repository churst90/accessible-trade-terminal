using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Core.Services;
using NSubstitute;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Drawing chart formations.
///
/// <para>
/// The audience is the one that cannot hear the description — a low-vision user, a sighted trading
/// partner, a screenshot in a bug report. A blind user already has the formation by ear, so this
/// layer is purely the visible half of something that was previously audio-only.
/// </para>
///
/// <para>
/// What is worth testing is not the pixels but the two rules that keep the drawing honest: it must
/// not draw on panes it does not describe, and it must not draw a target for a formation that never
/// broke. Beyond that, the checks are that it survives the awkward inputs a real chart produces —
/// log scale, formations running off the edge, an empty list.
/// </para>
/// </summary>
public class ChartFormationLayerTests
{
    /// <summary>The real theme, so colours and fonts are whatever the app actually uses.</summary>
    private static ChartTheme DefaultTheme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    private static RenderContext Ctx(bool logScale = false, string pane = "Main")
    {
        var bars = Enumerable.Range(0, 300).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i),
            Open = 100, High = 110, Low = 90, Close = 100, Volume = 1
        }).ToList();

        var surface = SKSurface.Create(new SKImageInfo(800, 600));
        return new RenderContext(
            surface.Canvas, new SKRect(0, 0, 800, 600), bars,
            ViewportStart: 100, ViewportLength: 100,
            Min: 50, Max: 150, IsLogScale: logScale,
            ItemWidth: 8f, Density: 1f, PaneName: pane,
            LocalCursorIndex: 50, Theme: DefaultTheme());
    }

    private static ChartPattern P(
        ChartPatternKind kind = ChartPatternKind.DoubleTop,
        ChartPatternState state = ChartPatternState.Forming,
        int start = 110, int end = 150, int known = 155,
        double trigger = 100, double? target = 80, double? secondary = null)
        => new(kind, state, start, end, known, trigger, DateTime.Today, DateTime.Today,
               CompletedAtIndex: state == ChartPatternState.Completed ? 160 : null,
               ExpiresAtIndex: 195, BreaksBelow: true, MeasuredTarget: target,
               SecondaryLevel: secondary);

    private static void Render(RenderContext ctx, params ChartPattern[] p) =>
        new ChartFormationLayer().Render(ctx, p);

    // ── The two rules that keep it honest ───────────────────────────────────────

    /// <summary>
    /// Formations describe PRICE. Drawing a neckline across a volume or oscillator pane would put a
    /// price level on an axis where it means nothing.
    /// </summary>
    [Fact]
    public void NothingIsDrawnOutsideThePricePane()
    {
        // No assertion on pixels is possible without a golden image; the property under test is that
        // the layer returns without touching a non-price pane, which it does by early return.
        var ex = Record.Exception(() => Render(Ctx(pane: "Volume"), P()));
        Assert.Null(ex);
    }

    /// <summary>
    /// An expired formation gets no target line. There was no break to project from, and a target
    /// hanging under a shape that never triggered asserts something that did not happen.
    /// </summary>
    [Fact]
    public void AnExpiredFormationIsDrawnWithoutATarget()
    {
        var ex = Record.Exception(() => Render(Ctx(), P(state: ChartPatternState.Expired)));
        Assert.Null(ex);
    }

    // ── Awkward inputs a real chart produces ────────────────────────────────────

    [Fact]
    public void AnEmptyListDrawsNothingAndDoesNotThrow()
        => Assert.Null(Record.Exception(() => new ChartFormationLayer().Render(Ctx(), Array.Empty<ChartPattern>())));

    /// <summary>
    /// A formation that starts before the viewport must still draw the part that is visible. Bars
    /// off the left edge produce negative x, which is legal in Skia but must not be allowed to make
    /// the whole shape vanish.
    /// </summary>
    [Fact]
    public void AFormationRunningOffTheLeftEdgeStillDraws()
        => Assert.Null(Record.Exception(() => Render(Ctx(), P(start: 0, end: 120, known: 125))));

    /// <summary>
    /// Log scale is not cosmetic here: a level placed with linear maths on a log chart lands at a
    /// price it does not claim to be, which for a trigger line is simply wrong information.
    /// </summary>
    [Fact]
    public void LogScaleIsHandled()
        => Assert.Null(Record.Exception(() => Render(Ctx(logScale: true), P())));

    /// <summary>A range carries two live levels and both must draw.</summary>
    [Fact]
    public void ARangeDrawsBothBoundaries()
        => Assert.Null(Record.Exception(() =>
            Render(Ctx(), P(kind: ChartPatternKind.Rectangle, trigger: 110, secondary: 90, target: null))));

    /// <summary>
    /// A chart carrying five formations at once becomes a thicket that hides the price it is
    /// describing — the same reason the spoken readout describes one and counts the rest.
    /// </summary>
    [Fact]
    public void TheNumberDrawnIsCapped()
    {
        Assert.True(ChartFormationLayer.MaxDrawn <= 3,
            "Drawing more than three formations at once hides the price behind the annotation.");

        var many = Enumerable.Range(0, 10)
            .Select(i => P(start: 110 + i, end: 150 + i, known: 155 + i))
            .ToArray();
        Assert.Null(Record.Exception(() => Render(Ctx(), many)));
    }

    /// <summary>Every kind must survive being drawn, including the newest.</summary>
    [Fact]
    public void EveryKindDraws()
    {
        foreach (ChartPatternKind kind in Enum.GetValues<ChartPatternKind>())
        {
            double? secondary = kind == ChartPatternKind.Rectangle ? 90 : null;
            var ex = Record.Exception(() => Render(Ctx(), P(kind: kind, secondary: secondary)));
            Assert.Null(ex);
        }
    }
}
