using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// L4-B: pins the browser-side mouse pipeline that lands drawing anchors on the chart.
/// js/keyboard.js#registerMouseHandler forwards (clientX-rectLeft, clientY-rectTop,
/// rect.width, rect.height) into <see cref="GlobalInputService.OnMouseEvent"/> →
/// <see cref="BlazorInputService.ProcessMouse"/> → <see cref="DrawingInteractionManager.HandleMouseEvent"/>.
/// These tests pin the .NET half of that contract: that synthetic (x, y, w, h)
/// tuples are mapped to (date, price) anchors and dispatch the expected store
/// actions, without needing a real browser or rendered chart.
/// </summary>
public sealed class DrawingInteractionManagerMouseDispatchTests
{
    private sealed class StubDrawingService : IDrawingService
    {
        public Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
            => new();
    }

    private sealed class TestHarness
    {
        public WorkspaceStore Store { get; }
        public BlazorInputService Input { get; }
        public SpyEventBus Bus { get; }
        public DrawingInteractionManager Manager { get; }
        public List<Ohlcv> Bars { get; }

        public TestHarness(int barCount = 200)
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
                    new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc).AddMinutes(i),
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
    }

    [Fact]
    public void MouseDown_after_keyboard_anchor1_routes_through_coordinate_math_and_commits_drawing()
    {
        // Mirrors the real browser flow when a user activates a multi-point drawing
        // via the keyboard chord (anchor 1 = chart cursor's bar) and then clicks the
        // mouse for anchor 2. We pin the full (x, y, w, h) → (date, price) → anchor 2
        // → CompleteDrawing → AddSeriesAction path: this is the hop where L4-B can
        // silently regress if either the JS-side coordinate math or the .NET-side
        // MapXToIndex/MapYToPrice fall out of agreement.
        var h = new TestHarness();
        // Anchor 1 lands at the keyboard cursor — UpdateDataAction(IsInitialLoad: true)
        // parks CurrentDataIndex at the live edge, so we record that to compare against.
        int cursorIndex = h.Store.State.CurrentDataIndex;
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);

        int seriesBefore = h.Store.State.ActiveSeries.Count;

        // Synthetic mouse-down at the centre of a 1280×720 viewport. The exact bar
        // x=640 maps to depends on viewport positioning at the live edge — we don't
        // hard-code it. The test pins the contract that *some* bar in the viewport
        // is reached and committed as anchor 2, which is what L4-B regression would
        // break (e.g. a w/h vs x/y argument swap, or dropping width/height entirely).
        h.Input.ProcessMouse(x: 640, y: 360, type: "MouseDown", width: 1280, height: 720);

        var drawingSeries = h.Store.State.ActiveSeries
            .FirstOrDefault(s => s.IsDrawing && s.Drawing?.Type == DrawingType.TrendLine);
        Assert.NotNull(drawingSeries);
        Assert.Equal(seriesBefore + 1, h.Store.State.ActiveSeries.Count);

        // Anchor 1 came from the keyboard path.
        Assert.Equal(h.Bars[cursorIndex].Date, drawingSeries!.Drawing!.AnchorDate1);
        // Anchor 2 came from the mouse path — a real bar's date and a price in range.
        Assert.NotNull(drawingSeries.Drawing.AnchorDate2);
        Assert.Contains(h.Bars, b => b.Date == drawingSeries.Drawing.AnchorDate2);
        Assert.InRange(
            drawingSeries.Drawing.AnchorPrice2 ?? double.NaN,
            h.Store.State.ViewportRange.Min,
            h.Store.State.ViewportRange.Max);
    }

    [Fact]
    public void MouseMove_with_no_pending_drawing_and_no_active_drag_is_a_noop()
    {
        // Pins the fast-reject in HandleMouseEvent. Without this guard, every
        // mouseover ripple of the chart would touch the workspace state and stir
        // up downstream subscribers — and registerMouseHandler in keyboard.js
        // fires MouseMove unconditionally, not just during drag.
        var h = new TestHarness();
        int actionsBefore = h.Bus.Log.Count;
        int seriesBefore = h.Store.State.ActiveSeries.Count;

        h.Input.ProcessMouse(x: 200, y: 200, type: "MouseMove", width: 1280, height: 720);

        Assert.Equal(actionsBefore, h.Bus.Log.Count);
        Assert.Equal(seriesBefore, h.Store.State.ActiveSeries.Count);
    }

    [Fact]
    public void MouseDown_with_x_past_right_margin_is_rejected_so_anchor2_does_not_land()
    {
        // The placement code allows future-space anchors inside RightMarginBars,
        // but anything past Data.Count + RightMarginBars - 1 has no defined date
        // and HandleMouseEvent returns early. This test pins that bound by feeding
        // an x that maps to an index far past the future-margin tail.
        var h = new TestHarness();
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);

        int seriesBefore = h.Store.State.ActiveSeries.Count;

        // Push x way past the viewport. MapXToIndex returns start + round(percent * (length-1)),
        // so x >> width drives the resulting index past the future-anchor allowance and the
        // dispatcher rejects the click. The drawing should stay in pending state.
        h.Input.ProcessMouse(x: 100_000, y: 360, type: "MouseDown", width: 1280, height: 720);

        Assert.Equal(seriesBefore, h.Store.State.ActiveSeries.Count);
    }

    [Fact]
    public void MouseDown_with_no_pending_drawing_and_no_drawings_in_workspace_is_a_noop()
    {
        // The first branch of HandleMouseDown — no pending placement, nothing to
        // edit-drag — must short-circuit cleanly. Otherwise an idle click on the
        // chart would publish a RedrawEvent or stir up the store every time.
        var h = new TestHarness();
        int seriesBefore = h.Store.State.ActiveSeries.Count;
        int eventsBefore = h.Bus.Log.Count;

        h.Input.ProcessMouse(x: 200, y: 200, type: "MouseDown", width: 1280, height: 720);

        Assert.Equal(seriesBefore, h.Store.State.ActiveSeries.Count);
        Assert.Equal(eventsBefore, h.Bus.Log.Count);
    }
}
