using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase B second pass: the on-demand chart hit-tester (click-to-focus a
/// component, component-aware context menu), shift+click range measurement,
/// and magnet snap. Design rule pinned throughout: precision is optional —
/// every hit-tested action has a fallback that works with imprecise pointing.
/// </summary>
public sealed class ChartHitTesterAndRangeTests
{
    private sealed class StubDrawingService : IDrawingService
    {
        public Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
            => new();
    }

    private sealed class Harness
    {
        public WorkspaceStore Store { get; }
        public BlazorInputService Input { get; }
        public SpyEventBus Bus { get; }
        public DrawingInteractionManager Manager { get; }
        public List<Ohlcv> Bars { get; }

        public Harness(int barCount = 200)
        {
            Bus = new SpyEventBus();
            Store = new WorkspaceStore(
                Bus,
                new ViewportRangeCalculator(),
                new ViewportNavigationService(),
                new VolumeStateService());
            Bars = new List<Ohlcv>();
            for (int i = 0; i < barCount; i++)
            {
                Bars.Add(new Ohlcv(
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                    100, 101, 99, 100, 1000));
            }
            Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Bars), IsInitialLoad: true));

            Input = new BlazorInputService();
            Manager = new DrawingInteractionManager(
                Bus,
                new StubDrawingService(),
                Store,
                new IndicatorModelFactory(new MockStylingService(), new MockIndicatorPreferencesService()),
                Input);
        }

        /// <summary>Adds an overlay line series in the Main pane with a constant value.</summary>
        public ChartSeries AddOverlayLine(string id, string name, double constantValue)
        {
            var config = new SeriesConfig { Id = id, Name = name, FriendlyName = name, Pane = "Main" };
            config.Components.Add(new ComponentConfig { Name = "Line" });
            var buffer = new SeriesDataBuffer { SeriesId = id };
            buffer.ComponentData["Line"] = Enumerable.Repeat(constantValue, Bars.Count).ToArray();
            var series = new ChartSeries(config, buffer);
            Store.Dispatch(new AddSeriesAction(series));
            return series;
        }
    }

    private static readonly (string, float)[] NoDividers = Array.Empty<(string, float)>();

    // ── ChartHitTester ───────────────────────────────────────────────────────

    [Fact]
    public void HitTest_FindsTheOverlayLine_WhenTheCursorIsOnIt()
    {
        var h = new Harness();
        h.AddOverlayLine("ema-1", "EMA 20", constantValue: 100.5);
        var state = h.Store.State;

        // Screen y of value 100.5 in the main range across a 720px pane.
        float lineY = ChartMath.MapY(100.5, 0, 720, state.ViewportRange.Min, state.ViewportRange.Max, false);

        var hit = ChartHitTester.HitTest(state, NoDividers, 0f, 0f, x: 640, y: lineY + 3, width: 1280, height: 720);

        Assert.NotNull(hit);
        Assert.Equal("ema-1", hit!.SeriesId);
        Assert.Equal("EMA 20", hit.SeriesName);
    }

    [Fact]
    public void HitTest_MissesWhenTheCursorIsFarFromEveryComponent()
    {
        var h = new Harness();
        h.AddOverlayLine("ema-1", "EMA 20", constantValue: 100.5);
        var state = h.Store.State;
        float lineY = ChartMath.MapY(100.5, 0, 720, state.ViewportRange.Min, state.ViewportRange.Max, false);

        // 100px above the line — far outside the 12px grab distance. Bar-value
        // components (candles at close=100) sit near the line too, so aim well away.
        double farY = Math.Max(5, lineY - 200);
        var hit = ChartHitTester.HitTest(state, NoDividers, 0f, 0f, x: 640, y: farY, width: 1280, height: 720);

        // Either nothing, or at least NOT the line we planted far away.
        if (hit != null) Assert.NotEqual("ema-1", hit.SeriesId);
    }

    [Fact]
    public void HitTest_IgnoresHiddenSeries_AndDrawings()
    {
        var h = new Harness();
        var line = h.AddOverlayLine("ema-1", "EMA 20", constantValue: 100.5);
        line.IsVisible = false;
        var state = h.Store.State;
        float lineY = ChartMath.MapY(100.5, 0, 720, state.ViewportRange.Min, state.ViewportRange.Max, false);

        var hit = ChartHitTester.HitTest(state, NoDividers, 0f, 0f, 640, lineY, 1280, 720);

        if (hit != null) Assert.NotEqual("ema-1", hit.SeriesId);
    }

    [Fact]
    public void HitTest_OverTheAxisStrip_ReturnsNothing()
    {
        var h = new Harness();
        h.AddOverlayLine("ema-1", "EMA 20", constantValue: 100.5);

        // Bottom 5% is the x-axis strip; cursor there must not hit chart components.
        var hit = ChartHitTester.HitTest(h.Store.State, NoDividers, 0.05f, 0f, 640, 719, 1280, 720);

        Assert.Null(hit);
    }

    // ── Click-to-focus ───────────────────────────────────────────────────────

    [Fact]
    public void Click_NearAnIndicatorLine_MovesKeyboardFocusToThatSeries()
    {
        var h = new Harness();
        h.AddOverlayLine("ema-1", "EMA 20", constantValue: 100.5);
        var state = h.Store.State;
        float lineY = ChartMath.MapY(100.5, 0, 720, state.ViewportRange.Min, state.ViewportRange.Max, false);

        h.Input.ProcessMouse(320, lineY, "MouseDown", 1280, 720);
        h.Input.ProcessMouse(320, lineY, "MouseUp", 1280, 720);

        Assert.Equal("ema-1", h.Store.State.FocusedSeriesId);
    }

    // ── Shift+click range measurement ────────────────────────────────────────

    [Fact]
    public void ShiftClick_SpeaksARangeSummary_WithoutMovingTheCursor()
    {
        var h = new Harness();
        int cursorBefore = h.Store.State.CurrentDataIndex;

        h.Input.ProcessMouse(320, 360, "MouseDown", 1280, 720);
        h.Input.ProcessMouse(320, 360, "ShiftMouseUp", 1280, 720);

        // Measuring never loses the user's place.
        Assert.Equal(cursorBefore, h.Store.State.CurrentDataIndex);
        var announcement = h.Bus.Log.OfType<AnnouncementEvent>().LastOrDefault();
        Assert.NotNull(announcement);
        Assert.StartsWith("Range:", announcement!.Message);
        Assert.Contains("bars", announcement.Message);
        Assert.Contains("High", announcement.Message);
        Assert.Contains("percent", announcement.Message);
    }

    [Fact]
    public void ShiftClick_InTheRightMargin_IsANoop()
    {
        var h = new Harness();
        h.Input.ProcessMouse(1279, 360, "MouseDown", 1280, 720);
        h.Input.ProcessMouse(1279, 360, "ShiftMouseUp", 1280, 720);

        Assert.DoesNotContain(h.Bus.Log.OfType<AnnouncementEvent>(),
            a => a.Message.StartsWith("Range:"));
    }

    // ── Magnet snap ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100.95, 101.0)] // near the high → snaps
    [InlineData(99.04, 99.0)]   // near the low → snaps
    [InlineData(100.02, 100.0)] // near close/open → snaps
    public void SnapToOhlc_PullsToTheNearestLevel_WithinTolerance(double raw, double expected)
    {
        var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);
        // Viewport span 2.0 → tolerance 0.06.
        Assert.Equal(expected, DrawingInteractionManager.SnapToOhlc(raw, bar, viewportSpan: 2.0), 6);
    }

    [Fact]
    public void SnapToOhlc_LeavesFarPricesUntouched()
    {
        var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);
        Assert.Equal(100.5, DrawingInteractionManager.SnapToOhlc(100.5, bar, viewportSpan: 2.0), 6);
    }

    // ── Context menu hit propagation ─────────────────────────────────────────

    [Fact]
    public void RightClick_NearAComponent_CarriesTheHitIntoTheMenuEvent()
    {
        var h = new Harness();
        h.AddOverlayLine("ema-1", "EMA 20", constantValue: 100.5);
        var state = h.Store.State;
        float lineY = ChartMath.MapY(100.5, 0, 720, state.ViewportRange.Min, state.ViewportRange.Max, false);

        h.Input.ProcessMouse(640, lineY, "ContextMenu", 1280, 720);

        var ev = h.Bus.Log.OfType<OpenChartContextMenuEvent>().LastOrDefault();
        Assert.NotNull(ev);
        Assert.Equal("ema-1", ev!.HitSeriesId);
    }

    /// <summary>
    /// With two lines inside the grab radius, the NEAREST one wins.
    ///
    /// <para>
    /// A2f's F24. Inverting <c>dist &lt; best.DistancePx</c> to <c>&gt;</c> — so the pointer
    /// selects the component furthest from where it was pointed — passed the entire suite,
    /// because every fixture in this file put exactly ONE line on the chart and with one
    /// candidate <c>best == null</c> short-circuits the comparison entirely.
    /// </para>
    ///
    /// <para>
    /// The same shape as A2e's E09 in a different file: <b>a selection rule is only under test
    /// when at least two candidates compete.</b>
    /// </para>
    /// </summary>
    [Fact]
    public void HitTest_PicksTheNearerOfTwoLinesInsideTheTolerance()
    {
        var h = new Harness();
        // 0.02 apart: about 7 px on a 720 px pane over this viewport, so both are inside the
        // 12 px grab radius and genuinely compete. 0.1 apart is 36 px and only one competes,
        // which is the fixture that makes this test vacuous — the guard below catches that.
        h.AddOverlayLine("far", "Far EMA", constantValue: 100.50);
        h.AddOverlayLine("near", "Near EMA", constantValue: 100.52);
        var state = h.Store.State;

        float farY  = ChartMath.MapY(100.50, 0, 720, state.ViewportRange.Min, state.ViewportRange.Max, false);
        float nearY = ChartMath.MapY(100.52, 0, 720, state.ViewportRange.Min, state.ViewportRange.Max, false);

        // Both are inside the 12 px tolerance; the cursor sits ON the nearer one.
        Assert.True(Math.Abs(farY - nearY) < ChartHitTester.TolerancePx,
            "Fixture error: the two lines must both be inside the grab radius, or only one competes.");

        var hit = ChartHitTester.HitTest(state, NoDividers, 0f, 0f, x: 640, y: nearY, width: 1280, height: 720);

        Assert.NotNull(hit);
        Assert.Equal("near", hit!.SeriesId);
    }

    /// <summary>
    /// A click on the x-axis strip is not a click on the last pane.
    ///
    /// <para>
    /// A2f's F25. The guard reads <c>yFrac &gt; plotBottomFrac</c>, where <c>plotBottomFrac</c>
    /// excludes the bottom axis strip — and widening it to <c>&gt; 1.0</c> passed everything,
    /// because every other test in this file passes <c>axisHeightFraction: 0f</c>. At zero the
    /// two expressions are identical, so the strip guard had never once been executed with a
    /// strip to guard.
    /// </para>
    /// </summary>
    [Fact]
    public void HitTest_RefusesAClickOnTheXAxisStrip()
    {
        var h = new Harness();
        const float axisHeight = 0.02f;                 // a thin bottom strip
        const float height = 720f;
        float plotBottomPx = height * (1f - axisHeight);

        // The line sits at the BOTTOM of the viewport, so it maps to the very bottom edge of the
        // plot area — right up against the strip. Any higher and a click inside the strip is out
        // of grab range anyway, and the test passes for the wrong reason: the first draft of this
        // test put the line at 100.5, ~36 px away, so the original and the mutant both returned
        // null and the mutant survived its own kill.
        double lineValue = h.Store.State.ViewportRange.Min;
        h.AddOverlayLine("floor", "Floor", constantValue: lineValue);
        // Pin the range floor to the line. Auto-fit includes Main-pane overlays, so adding a line
        // AT the range minimum pushes the minimum below it and the line leaves the bottom edge.
        // A test of the HIT TESTER wants the geometry to be a function of the fixture alone —
        // the auto-fit policy is tested in PriceAutoFitScopeTests.
        var state = h.Store.State with
        {
            ViewportRange = (lineValue, h.Store.State.ViewportRange.Max),
        };

        float lineY = ChartMath.MapY(lineValue, 0, plotBottomPx,
                                     state.ViewportRange.Min, state.ViewportRange.Max, false);
        double clickY = lineY + 4;                      // inside the strip, 4 px from the line

        Assert.True(clickY / height > 1f - axisHeight,
            "Fixture error: the click must land INSIDE the x-axis strip, or the guard is not exercised.");
        Assert.True(Math.Abs(lineY - clickY) < ChartHitTester.TolerancePx,
            "Fixture error: the click must be within grab range of the line, or a null result proves nothing.");

        var hit = ChartHitTester.HitTest(state, NoDividers, axisHeight, 0f,
                                         x: 640, y: clickY, width: 1280, height: (double)height);

        Assert.Null(hit);
    }
}
