using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

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

        // Push x way past the viewport. MapXToIndex returns start + floor(percent * length),
        // so x >> width drives the resulting index past the future-anchor allowance and the
        // dispatcher rejects the click. The drawing should stay in pending state.
        h.Input.ProcessMouse(x: 100_000, y: 360, type: "MouseDown", width: 1280, height: 720);

        Assert.Equal(seriesBefore, h.Store.State.ActiveSeries.Count);
    }

    [Fact]
    public void MouseDown_with_no_pending_drawing_and_no_drawings_in_workspace_is_a_noop()
    {
        // The first branch of HandleMouseDown — no pending placement, nothing to
        // edit-drag — must not add series or publish events on the down alone. It
        // arms a viewport pan-drag (silent until the first MouseMove), so an idle
        // click that never moves leaves the workspace untouched.
        var h = new TestHarness();
        int seriesBefore = h.Store.State.ActiveSeries.Count;
        int eventsBefore = h.Bus.Log.Count;

        h.Input.ProcessMouse(x: 200, y: 200, type: "MouseDown", width: 1280, height: 720);

        Assert.Equal(seriesBefore, h.Store.State.ActiveSeries.Count);
        Assert.Equal(eventsBefore, h.Bus.Log.Count);
    }

    // ── Viewport pan-drag (grab-and-slide on empty chart space) ──────────────

    [Fact]
    public void Drag_on_empty_chart_with_no_tool_pans_the_viewport_back_in_time()
    {
        // No drawing tool armed and no anchor handle under the cursor → a mouse-down
        // grabs the chart. Dragging right pulls the chart right, revealing older bars,
        // which is a decrease in ViewportStartIndex. Pins the pan-drag branch added to
        // HandleMouseDown/UpdatePan.
        var h = new TestHarness();
        int startBefore = h.Store.State.ViewportStartIndex;
        Assert.True(startBefore > 0, "Test needs headroom to pan left; viewport should start at the live edge.");

        h.Input.ProcessMouse(x: 200, y: 360, type: "MouseDown", width: 1280, height: 720);
        h.Input.ProcessMouse(x: 700, y: 360, type: "MouseMove", width: 1280, height: 720); // dx = +500px → drag right

        Assert.True(h.Store.State.ViewportStartIndex < startBefore,
            "Dragging right should pan the viewport back in time (start index decreases).");
    }

    [Fact]
    public void Pan_drag_stops_dispatching_after_mouse_up()
    {
        // Once the drag ends, a subsequent (hover) MouseMove — which keyboard.js fires
        // unconditionally — must not keep panning. Pins that MouseUp clears _isPanning.
        var h = new TestHarness();
        h.Input.ProcessMouse(x: 200, y: 360, type: "MouseDown", width: 1280, height: 720);
        h.Input.ProcessMouse(x: 700, y: 360, type: "MouseMove", width: 1280, height: 720);
        h.Input.ProcessMouse(x: 700, y: 360, type: "MouseUp",   width: 1280, height: 720);

        int startAfterRelease = h.Store.State.ViewportStartIndex;
        h.Input.ProcessMouse(x: 1000, y: 360, type: "MouseMove", width: 1280, height: 720); // stray hover move

        Assert.Equal(startAfterRelease, h.Store.State.ViewportStartIndex);
    }

    [Fact]
    public void Drag_while_a_drawing_tool_is_armed_does_not_pan()
    {
        // With a placement flow active, a drag belongs to the drawing, not the viewport.
        // The pan branch is only reached when nothing is pending and no handle is grabbed.
        var h = new TestHarness();
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);
        int startBefore = h.Store.State.ViewportStartIndex;

        h.Input.ProcessMouse(x: 200, y: 360, type: "MouseDown", width: 1280, height: 720);
        h.Input.ProcessMouse(x: 700, y: 360, type: "MouseMove", width: 1280, height: 720);

        Assert.Equal(startBefore, h.Store.State.ViewportStartIndex);
    }

    // ── Touch-only accessible drawing (no mouse, no keyboard chord) ───────────

    [Fact]
    public void Touch_place_anchor_completes_a_two_point_drawing_without_the_mouse()
    {
        // The mobile flow: pick the tool (anchor 1 at cursor), move the cursor with the
        // touch bar's ◀▶, then tap "Place drawing point" for anchor 2 — no keyboard.
        var h = new TestHarness();
        Assert.False(h.Manager.HasPendingDrawing);

        int c1 = h.Store.State.CurrentDataIndex;
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);   // anchor 1
        Assert.True(h.Manager.HasPendingDrawing);

        h.Store.Dispatch(new SetCursorAction(c1 - 20));     // move to a different bar
        h.Manager.PlaceAnchorAtCursor(h.Bars);              // anchor 2 → completes

        var drawing = h.Store.State.ActiveSeries
            .FirstOrDefault(s => s.IsDrawing && s.Drawing?.Type == DrawingType.TrendLine);
        Assert.NotNull(drawing);
        Assert.False(h.Manager.HasPendingDrawing);
    }

    [Fact]
    public void Place_anchor_with_no_pending_drawing_speaks_a_hint_and_adds_nothing()
    {
        var h = new TestHarness();
        int seriesBefore = h.Store.State.ActiveSeries.Count;

        h.Manager.PlaceAnchorAtCursor(h.Bars);

        Assert.False(h.Manager.HasPendingDrawing);
        Assert.Equal(seriesBefore, h.Store.State.ActiveSeries.Count);
        Assert.Contains(h.Bus.Log, e => e is AnnouncementEvent a && a.Message.Contains("No drawing in progress"));
    }

    // ── Anchor handles sit where the drawing is drawn ──────────────────────────
    //
    // A handle used to be hit-tested at the LEFT EDGE of its bar's slot while the renderer draws
    // the bar at the slot's centre (half a bar off, 6.4 px at 100 bars over 1280 px against a
    // 10 px grab radius), and a date past the last bar was put ON the last bar, so a trend
    // line's forward anchor could not be picked up where it was drawn. A vertical line is used
    // because its handle is date-only: the cursor's own y stands in, so x is all that is tested.

    private static void AddVerticalLine(TestHarness h, DateTime date)
    {
        var series = new ChartSeries(
            new SeriesConfig { Id = "v1", Name = "Vertical", FriendlyName = "Vertical line", Pane = "main", IndicatorCode = "VerticalLine" },
            new SeriesDataBuffer { SeriesId = "v1" })
        {
            Drawing = new DrawingData { Type = DrawingType.VerticalLine, AnchorDate1 = date },
        };
        h.Store.Dispatch(new AddSeriesAction(series));
    }

    private static double SlotCentre(TestHarness h, int bar, double width)
    {
        var s = h.Store.State;
        // Written out, not asked of ChartMath: the renderer's slot of width / length, centred.
        return (bar - s.ViewportStartIndex + 0.5) * width / s.ViewportLength;
    }

    [Fact]
    public void A_handle_is_grabbed_at_its_bars_centre_not_its_slots_left_edge()
    {
        var h = new TestHarness();
        var s = h.Store.State;
        int bar = s.ViewportStartIndex + s.ViewportLength / 3;
        AddVerticalLine(h, h.Bars[bar].Date);
        double slot = 1280.0 / s.ViewportLength;
        // The click below is 0.4 slot right of the bar's centre and 0.9 slot right of the slot's
        // left edge: inside the grab radius only if the handle is at the centre.
        Assert.True(0.4 * slot < 10 && 0.9 * slot > 10, $"slot {slot} px does not tell the two apart");

        h.Manager.HandleMouseEvent(SlotCentre(h, bar, 1280) + 0.4 * slot, 300, "ContextMenu", 1280, 720);

        var opened = Assert.Single(h.Bus.Log.OfType<OpenDrawingContextMenuEvent>());
        Assert.Equal("v1", opened.SeriesId);
    }

    [Fact]
    public void A_handle_past_the_last_bar_is_grabbed_where_it_is_drawn()
    {
        var h = new TestHarness();
        var s = h.Store.State;
        int future = h.Bars.Count - 1 + 5;
        Assert.True(future < s.ViewportStartIndex + s.ViewportLength, "the right margin is not on screen");
        AddVerticalLine(h, DrawingInteractionManager.ProjectFutureDate(h.Bars, 5));

        h.Manager.HandleMouseEvent(SlotCentre(h, future, 1280), 300, "ContextMenu", 1280, 720);

        var opened = Assert.Single(h.Bus.Log.OfType<OpenDrawingContextMenuEvent>());
        Assert.Equal("v1", opened.SeriesId);
    }

    // ── A click that cannot do anything says so ─────────────────────────────────
    //
    // Each of these used to return without a sound, so a blind mouse user could not tell a
    // refused click from one that never reached the chart.

    private static List<string> Spoken(TestHarness h)
        => h.Bus.Log.OfType<AnnouncementEvent>().Select(a => a.Message).ToList();

    [Fact]
    public void A_point_past_the_room_for_points_is_refused_aloud()
    {
        var h = new TestHarness();
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);
        int seriesBefore = h.Store.State.ActiveSeries.Count;
        int before = Spoken(h).Count;

        h.Input.ProcessMouse(x: 100_000, y: 360, type: "MouseDown", width: 1280, height: 720);

        Assert.Equal(seriesBefore, h.Store.State.ActiveSeries.Count);
        var said = Assert.Single(Spoken(h).Skip(before));
        Assert.Equal(DrawingInteractionManager.OutOfRoom(int.MaxValue, h.Store.State.RightMarginBars), said);
        Assert.Contains($"{h.Store.State.RightMarginBars} bars after the last bar", said);
    }

    [Fact]
    public void Moving_past_the_room_for_points_stays_quiet()
    {
        var h = new TestHarness();
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);
        int before = Spoken(h).Count;

        h.Input.ProcessMouse(x: 100_000, y: 360, type: "MouseMove", width: 1280, height: 720);

        Assert.Equal(before, Spoken(h).Count);
    }

    [Fact]
    public void A_press_past_the_bars_with_no_tool_armed_stays_quiet()
    {
        var h = new TestHarness();
        int before = h.Bus.Log.Count;

        h.Input.ProcessMouse(x: 100_000, y: 360, type: "MouseDown", width: 1280, height: 720);

        Assert.Equal(before, h.Bus.Log.Count);
    }

    [Fact]
    public void A_press_before_the_bars_arrive_says_none_are_loaded()
    {
        var h = new TestHarness(barCount: 0);
        int before = Spoken(h).Count;

        h.Input.ProcessMouse(x: 200, y: 200, type: "MouseDown", width: 1280, height: 720);
        h.Input.ProcessMouse(x: 210, y: 200, type: "MouseMove", width: 1280, height: 720);

        Assert.Equal(new[] { DrawingInteractionManager.NoBarsYet }, Spoken(h).Skip(before));
    }

    [Fact]
    public void A_measure_onto_an_empty_slot_says_there_is_no_bar()
    {
        var h = new TestHarness();
        var s = h.Store.State;
        int future = h.Bars.Count - 1 + 3;
        Assert.True(future < s.ViewportStartIndex + s.ViewportLength, "the right margin is not on screen");
        int before = Spoken(h).Count;

        h.Input.ProcessMouse(x: SlotCentre(h, future, 1280), y: 300, type: "ShiftMouseUp", width: 1280, height: 720);

        Assert.Equal(new[] { DrawingInteractionManager.NoBarToMeasure }, Spoken(h).Skip(before));
    }

    [Fact]
    public void A_measure_onto_a_bar_still_reads_the_range()
    {
        var h = new TestHarness();
        int bar = h.Bars.Count - 10;
        int before = Spoken(h).Count;

        h.Input.ProcessMouse(x: SlotCentre(h, bar, 1280), y: 300, type: "ShiftMouseUp", width: 1280, height: 720);

        Assert.StartsWith("Range: ", Assert.Single(Spoken(h).Skip(before)));
    }

    [Theory]
    [InlineData(-1, 20, "Before the first bar; a point can't go there.")]
    [InlineData(500, 0, "Past the last bar; a point can't go there.")]
    [InlineData(500, 20, "Past the room for points; a point can go up to 20 bars after the last bar.")]
    public void A_refused_point_names_which_side_it_fell_off(int index, int margin, string expected)
        => Assert.Equal(expected, DrawingInteractionManager.OutOfRoom(index, margin));
}
