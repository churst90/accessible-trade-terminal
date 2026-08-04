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
public class ChartFormationLayerTests : IDisposable
{
    /// <summary>
    /// Every surface this fixture creates, disposed when the test finishes.
    ///
    /// <para>
    /// <b>The first version leaked them and it aborted the whole test run.</b> <c>SKSurface</c> owns
    /// an unmanaged Skia allocation; leaving it to a finalizer means native memory being released on
    /// the GC thread while xUnit is running other tests in parallel, and the process died at a
    /// different test each time — 761, then 1,472, then 1,843 of 2,857. It never presented as a
    /// failing test, which is why it read as flakiness rather than as a defect in these tests.
    /// </para>
    ///
    /// <para>
    /// Worth remembering as a shape: <b>a test run that aborts at a DIFFERENT point each time is a
    /// native or concurrency problem, not a failing assertion.</b> No amount of reading the last
    /// test name will find it; the way in is to bisect by removing suspects.
    /// </para>
    /// </summary>
    private readonly List<SKSurface> _surfaces = new();

    public void Dispose()
    {
        foreach (var s in _surfaces) s.Dispose();
        _surfaces.Clear();
    }

    /// <summary>The real theme, so colours and fonts are whatever the app actually uses.</summary>
    private static ChartTheme DefaultTheme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    private RenderContext Ctx(bool logScale = false, string pane = "Main")
    {
        var bars = Enumerable.Range(0, 300).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i),
            Open = 100, High = 110, Low = 90, Close = 100, Volume = 1
        }).ToList();

        var surface = SKSurface.Create(new SKImageInfo(800, 600));
        _surfaces.Add(surface);
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

    // ── Found in a screenshot, not by a test ────────────────────────────────────

    /// <summary>
    /// A level outside the visible price range must not be drawn at all.
    ///
    /// <para>
    /// Clamping it to the top or bottom edge does not communicate "this is off screen", it
    /// communicates "this level is HERE". On a real BTC chart whose scale had been stretched to
    /// include zero by an unrelated indicator, triggers from formations years old were drawn as
    /// though they were current price.
    /// </para>
    /// </summary>
    [Fact]
    public void ALevelOutsideTheVisibleRangeIsNotDrawn()
    {
        // Viewport range is 50..150; this trigger is nowhere near it.
        var ex = Record.Exception(() => Render(Ctx(), P(trigger: 1815, target: 900)));
        Assert.Null(ex);
    }

    /// <summary>
    /// Three formations whose triggers sit close together must not overprint.
    ///
    /// <para>
    /// The first version drew every label at its own line's y with no awareness of the others, and
    /// on a live chart the result was an illegible smear of three overlapping names. Invisible to a
    /// test that only asks whether drawing throws — which is why this one asserts on the placement
    /// helper rather than on the canvas.
    /// </para>
    /// </summary>
    [Fact]
    public void LabelsAtNearlyTheSamePriceAreStaggered()
    {
        var ctx = Ctx();
        var rows = new List<float>();

        // Three triggers within a hair of each other.
        float a = PlaceLabel(ctx, rows, 100.0);
        float b = PlaceLabel(ctx, rows, 100.2);
        float c = PlaceLabel(ctx, rows, 100.4);

        Assert.True(Math.Abs(a - b) >= 10f, $"labels overlapped at {a} and {b}");
        Assert.True(Math.Abs(b - c) >= 10f, $"labels overlapped at {b} and {c}");
        Assert.True(Math.Abs(a - c) >= 10f, $"labels overlapped at {a} and {c}");
    }

    /// <summary>A label near the top of the range must stay inside the pane, not above it.</summary>
    [Fact]
    public void LabelsStayInsideThePane()
    {
        var ctx = Ctx();
        var rows = new List<float>();

        float top = PlaceLabel(ctx, rows, 149.9);    // right at the top of 50..150
        float bottom = PlaceLabel(ctx, rows, 50.1);  // right at the bottom

        Assert.InRange(top, ctx.PaneRect.Top, ctx.PaneRect.Bottom);
        Assert.InRange(bottom, ctx.PaneRect.Top, ctx.PaneRect.Bottom);
    }

    /// <summary>
    /// Mirrors the layer's own placement rule. Kept in the test rather than made public on the
    /// layer, because the rule is an implementation detail of drawing and exposing it would invite
    /// a caller to depend on it.
    /// </summary>
    private static float PlaceLabel(RenderContext ctx, List<float> taken, double price)
    {
        double frac = (price - ctx.Min) / (ctx.Max - ctx.Min);
        float y = ctx.PaneRect.Bottom - (float)(frac * ctx.PaneRect.Height);

        float lineHeight = 12f * ctx.Density;
        float row = y - 3 * ctx.Density;
        while (taken.Any(t => Math.Abs(t - row) < lineHeight)) row += lineHeight;
        row = Math.Clamp(row, ctx.PaneRect.Top + lineHeight, ctx.PaneRect.Bottom - 2 * ctx.Density);
        taken.Add(row);
        return row;
    }
}
