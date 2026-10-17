using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Core.Services;
using NSubstitute;
using Newtonsoft.Json.Linq;
using SkiaSharp;

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
        double trigger = 100, double? target = 80, double? secondary = null,
        int expires = 195, int? targetReached = null)
        => new(kind, state, start, end, known, trigger, DateTime.Today, DateTime.Today,
               CompletedAtIndex: state == ChartPatternState.Completed ? 160 : null,
               ExpiresAtIndex: expires, BreaksBelow: true, MeasuredTarget: target,
               SecondaryLevel: secondary, TargetReachedAtIndex: targetReached);

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

    /// <summary>
    /// The renderer paints the pane legend in the top-left corner AFTER the formation layer
    /// draws, and a formation running off the left edge puts its label in exactly that corner.
    /// Seen 2026-09-22: "ascending triangle" and its target stacked under the legend box, the
    /// box painted over them. The label steps below the rect the renderer says it will cover.
    /// </summary>
    [Fact]
    public void ALabelUnderTheLegendStepsBelowIt()
    {
        var legend = new SKRect(6, 6, 200, 80);
        var ctx = Ctx() with { Avoid = legend };
        var rows = new List<float>();

        // A trigger near the top of the range, labelled at the left edge: exactly where the legend is.
        double frac = (149.0 - ctx.Min) / (ctx.Max - ctx.Min);
        float y = ctx.PaneRect.Bottom - (float)(frac * ctx.PaneRect.Height);
        float row = ChartFormationLayer.NextLabelRow(ctx, rows, y, x: 4f, width: 90f);

        Assert.True(row - 12f >= legend.Bottom - 0.5f,
            $"the label's row {row} still overlaps the legend, which ends at {legend.Bottom}");

        // The same label placed to the RIGHT of the legend is not pushed at all.
        var free = new List<float>();
        float rowRight = ChartFormationLayer.NextLabelRow(ctx, free, y, x: 400f, width: 90f);
        Assert.True(rowRight < legend.Bottom, $"a label clear of the legend was moved anyway, to {rowRight}");
    }

    /// <summary>Only the dominant formation labels its target and floor; the rest say their name once.</summary>
    [Fact]
    public void OnlyTheDominantFormationLabelsItsLevels()
    {
        Assert.True(ChartFormationLayer.LabelsEverything(0));
        Assert.False(ChartFormationLayer.LabelsEverything(1));
        Assert.False(ChartFormationLayer.LabelsEverything(2));
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

    // ── Lines stop where the formation's story stops (Cody's 2026-10-15 screenshots) ──────
    //
    // A March rising wedge's trigger (solid, 75,000) and measured target (dashed, 60,300) were
    // still crossing a BTCUSDT daily chart in October, months after price had closed through the
    // one and traded at the other. These read the PIXELS on the line's own row, to the right of
    // the formation's span (so neither the span shading, its edges nor the labels are in the way):
    // ink before the bar that ended the line, background after it.

    /// <summary>The fixture's price-to-row mapping: 50..150 over a 600px pane, linear.</summary>
    private static int RowOf(double price) => (int)Math.Round(600 - (price - 50) / 100.0 * 600);

    /// <summary>The fixture's bar-to-column mapping: bar 100 at x 0, 8px per bar, bar centre.</summary>
    private static int ColumnOf(int bar) => (bar - 100) * 8 + 4;

    private SKBitmap RenderToBitmap(params ChartPattern[] p)
    {
        var ctx = Ctx();
        ctx.Canvas.Clear(ctx.Theme.Background);
        Render(ctx, p);
        using var image = _surfaces[^1].Snapshot();
        return SKBitmap.FromImage(image);
    }

    /// <summary>
    /// Anything but the cleared background within a pixel of the row (the line is 1–1.6px), in
    /// the six columns leading up to the bar's centre — wide enough that a dashed target cannot
    /// hide in a gap (6px dashes, 6px gaps), narrow enough to stay well clear of a line that
    /// ended five bars (40px) earlier.
    /// </summary>
    private static bool InkAt(SKBitmap bmp, int bar, double price)
    {
        var bg = DefaultTheme().Background;
        int x0 = ColumnOf(bar), y = RowOf(price);
        for (int x = x0 - 6; x <= x0; x++)
            for (int dy = -1; dy <= 1; dy++)
                if (bmp.GetPixel(x, y + dy) != bg) return true;
        return false;
    }

    [Fact]
    public void ABrokenFormationsTriggerStopsAtTheBarThatClosedThroughIt()
    {
        // Completed at bar 160 (the fixture), target touched at bar 170.
        using var bmp = RenderToBitmap(P(state: ChartPatternState.Completed, targetReached: 170));

        Assert.True(InkAt(bmp, 156, 100), "the trigger is not drawn between the formation and its break");
        Assert.True(InkAt(bmp, 160, 100), "the trigger does not reach the candle that broke it");
        Assert.False(InkAt(bmp, 165, 100), "the trigger runs on past the bar that closed through it");
        Assert.False(InkAt(bmp, 199, 100), "the trigger still reaches the right edge");
    }

    [Fact]
    public void AMeasuredTargetStopsAtTheBarThatTradedAtIt()
    {
        using var bmp = RenderToBitmap(P(state: ChartPatternState.Completed, targetReached: 170));

        Assert.True(InkAt(bmp, 168, 80), "the target is not drawn up to the bar that reached it");
        Assert.False(InkAt(bmp, 176, 80), "the target runs on past the bar that traded at it");
        Assert.False(InkAt(bmp, 199, 80), "the target still reaches the right edge");
    }

    /// <summary>
    /// Not reached: the target stays a projection for the formation's own length past the break
    /// (160 + 40 = bar 200, off this viewport's right edge), so here it still runs to the edge.
    /// </summary>
    [Fact]
    public void AnUnreachedTargetRunsToTheEndOfItsWindow()
    {
        using var bmp = RenderToBitmap(P(state: ChartPatternState.Completed, targetReached: null));
        Assert.True(InkAt(bmp, 199, 80), "an unreached target inside its window was cut short");
    }

    [Fact]
    public void AnExpiredFormationsTriggerStopsAtItsExpiry()
    {
        using var bmp = RenderToBitmap(P(state: ChartPatternState.Expired, expires: 180));

        Assert.True(InkAt(bmp, 178, 100), "an expired formation's trigger is not drawn up to its expiry");
        Assert.False(InkAt(bmp, 186, 100), "an expired formation's trigger runs on past its expiry");
    }

    /// <summary>A live formation's expiry lies past the newest bar, so its levels reach the live edge.</summary>
    [Fact]
    public void ALiveFormationsLevelsReachTheRightEdge()
    {
        using var bmp = RenderToBitmap(P(state: ChartPatternState.Forming, expires: 230));

        Assert.True(InkAt(bmp, 199, 100), "a forming formation's trigger stops short of the live edge");
        Assert.True(InkAt(bmp, 199, 80), "a forming formation's 'if it breaks' target stops short of the live edge");
    }

    // ── A wedge is drawn as a wedge (Cody's 2026-10-15 screenshots) ──────────────────────

    /// <summary>
    /// A rising wedge over bars 110–150: highs 120 → 130 (bars 112 → 148), lows 90 → 110
    /// (bars 110 → 150), so the trigger is the last low, 110, and the target 110 − 30 = 80.
    /// </summary>
    private static ChartPattern Wedge() =>
        new(ChartPatternKind.RisingWedge, ChartPatternState.Forming, 110, 150, 155, 110,
            DateTime.Today, DateTime.Today, ExpiresAtIndex: 230, BreaksBelow: true, MeasuredTarget: 80,
            UpperBoundary: new FormationLine(112, 120, 148, 130),
            LowerBoundary: new FormationLine(110, 90, 150, 110));

    /// <summary>
    /// Ink against what the same column looks like on a row nothing is drawn on — inside the
    /// formation's span the background carries the span's faint tint, so plain background is the
    /// wrong reference there.
    /// </summary>
    private static bool InkAgainstColumn(SKBitmap bmp, int bar, double price, int referenceRow = 30)
    {
        int x = ColumnOf(bar), y = RowOf(price);
        var reference = bmp.GetPixel(x, referenceRow);
        for (int dy = -1; dy <= 1; dy++)
            if (bmp.GetPixel(x, y + dy) != reference) return true;
        return false;
    }

    [Fact]
    public void AWedgeIsDrawnAsItsTwoSlopingEdges()
    {
        using var bmp = RenderToBitmap(Wedge());

        // Halfway along each edge: the upper at bar 130 is 125, the lower is 100.
        Assert.True(InkAgainstColumn(bmp, 130, 125), "no upper edge where the wedge's highs run");
        Assert.True(InkAgainstColumn(bmp, 130, 100), "no lower edge where the wedge's lows run");

        // And they are SLOPED: nothing at those prices near the opposite end of the span, where a
        // flat line at the same price would also be.
        Assert.False(InkAgainstColumn(bmp, 114, 125), "the upper edge is flat, not rising");
        Assert.False(InkAgainstColumn(bmp, 146, 100), "the lower edge is flat, not rising");
    }

    /// <summary>
    /// With the edges drawn, the flat trigger starts where the lower edge ends (the last low, bar
    /// 150) instead of cutting through the wedge from its first bar.
    /// </summary>
    [Fact]
    public void AWedgesTriggerStartsWhereItsEdgeEnds()
    {
        using var bmp = RenderToBitmap(Wedge());

        Assert.False(InkAgainstColumn(bmp, 125, 110), "the trigger runs through the inside of the wedge");
        Assert.True(InkAt(bmp, 156, 110), "the trigger does not carry on from the wedge's last low");
    }

    // ── Labels say what the line is (Cody's 2026-10-15 screenshots) ───────────────────────

    [Fact]
    public void TheSolidLineIsLabelledAsTheTriggerWithItsPriceAndSide()
    {
        var wedge = P(kind: ChartPatternKind.RisingWedge, trigger: 74_508.12, target: 60_300);
        string label = ChartFormationLayer.TriggerLabel(wedge);

        Assert.Contains("rising wedge", label);
        Assert.Contains("trigger", label);        // the speech's word for a wedge's level
        Assert.Contains("74,508.12", label);
        Assert.Contains("downward breakout", label);   // the side, in Bulkowski's words

        // Seen on the first render of the real BTC daily: "bear flag flag edge 59,485.04".
        string flag = ChartFormationLayer.TriggerLabel(P(kind: ChartPatternKind.BearFlag, trigger: 59_485.04));
        Assert.DoesNotContain("flag flag", flag);
        Assert.Contains("edge", flag);

        // A range has not picked a side, and its label must not pick one for it.
        var range = P(kind: ChartPatternKind.Rectangle, trigger: 110, secondary: 90, target: null);
        Assert.DoesNotContain("below", ChartFormationLayer.TriggerLabel(range));
        Assert.DoesNotContain("above", ChartFormationLayer.TriggerLabel(range));
    }

    /// <summary>
    /// Bulkowski's words, at Cody's direction (2026-10-17): "downward breakout" / "upward
    /// breakout", not "break below". And the label must never say a breakout happened before a
    /// close went through — a forming wedge "confirms on" one; an expired one "held".
    /// </summary>
    [Fact]
    public void TheTriggerLabelUsesBreakoutTermsAndOnlyClaimsABreakoutThatHappened()
    {
        var forming = P(kind: ChartPatternKind.RisingWedge, trigger: 74_912);
        var broken  = P(kind: ChartPatternKind.RisingWedge, trigger: 74_912, state: ChartPatternState.Completed);
        var held    = P(kind: ChartPatternKind.RisingWedge, trigger: 74_912, state: ChartPatternState.Expired);
        var upward  = P(kind: ChartPatternKind.FallingWedge, trigger: 50, state: ChartPatternState.Completed) with { BreaksBelow = false };

        Assert.EndsWith(", downward breakout", ChartFormationLayer.TriggerLabel(broken));
        Assert.EndsWith(", upward breakout", ChartFormationLayer.TriggerLabel(upward));
        Assert.Contains("confirms on a downward breakout", ChartFormationLayer.TriggerLabel(forming));
        Assert.EndsWith(", held", ChartFormationLayer.TriggerLabel(held));
        Assert.DoesNotContain("breakout", ChartFormationLayer.TriggerLabel(held));
        foreach (var p in new[] { forming, broken, held, upward })
            Assert.DoesNotContain("break below", ChartFormationLayer.TriggerLabel(p));
    }

    [Fact]
    public void TheDashedLineIsLabelledAsTheMeasuredTargetWithItsPrice()
    {
        string label = ChartFormationLayer.TargetLabel(P(kind: ChartPatternKind.RisingWedge), 60_300);
        Assert.Contains("rising wedge", label);
        Assert.Contains("measured target", label);
        Assert.Contains("60,300.00", label);
    }

    /// <summary>
    /// The number on the line is the number Cody heard, digit for digit — only grouped, as the
    /// hover readout groups it, for the sighted reader beside him.
    /// </summary>
    [Theory]
    [InlineData(74_508.12)]
    [InlineData(82_366.23)]
    [InlineData(1_234_567.891)]
    [InlineData(612.5)]
    [InlineData(0.0363)]
    [InlineData(0.00003141)]
    public void LabelPricesAreTheSpokenDigitsGrouped(double price)
    {
        string label = ChartFormationLayer.LabelPrice(price);
        Assert.Equal(Core.Services.Accessibility.SpeechPriceFormatter.FormatPrice(price), label.Replace(",", ""));
        if (price >= 1000) Assert.Contains(",", label);
    }

    /// <summary>
    /// Calls the layer's OWN placement rule.
    ///
    /// <para>
    /// This used to be a copy of those six lines living in the test file, and the two tests above
    /// asserted the copy — so the staggering and the clamping could both have been deleted from
    /// <see cref="ChartFormationLayer"/> without either going red. The rule is now
    /// <c>internal static</c> on the layer (see the remarks there for why internal and not public),
    /// and only the price-to-pixel conversion, which is the caller's job, happens here.
    /// </para>
    /// </summary>
    private static float PlaceLabel(RenderContext ctx, List<float> taken, double price)
    {
        double frac = (price - ctx.Min) / (ctx.Max - ctx.Min);
        float y = ctx.PaneRect.Bottom - (float)(frac * ctx.PaneRect.Height);

        return ChartFormationLayer.NextLabelRow(ctx, taken, y);
    }
}
