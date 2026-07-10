using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase B mouse interactions: click-a-bar-to-hear-it, the chart-level context
/// menu event, shift+wheel pan, double-click jump-to-live, and the hover
/// crosshair tracker. The design rule pinned throughout: every mouse action
/// lands in the SAME store state the keyboard navigates, so speech and
/// sonification fire identically for mouse and keyboard users.
/// </summary>
public sealed class ChartMouseInteractionTests
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

        public void Click(double x, double y, double w = 1280, double h = 720)
        {
            Input.ProcessMouse(x, y, "MouseDown", w, h);
            Input.ProcessMouse(x, y, "MouseUp", w, h);
        }
    }

    // ── Click a bar to select + hear it ──────────────────────────────────────

    [Fact]
    public void Click_on_empty_chart_moves_the_keyboard_cursor_to_the_clicked_bar()
    {
        var h = new Harness();
        int before = h.Store.State.CurrentDataIndex;

        // Click at 25% across the viewport — well away from the live-edge cursor.
        h.Click(320, 360);

        int after = h.Store.State.CurrentDataIndex;
        Assert.NotEqual(before, after);
        int expected = ChartMath.MapXToIndex(
            320, 1280, h.Store.State.ViewportStartIndex, h.Store.State.ViewportLength);
        Assert.Equal(expected, after);
    }

    [Fact]
    public void Click_fires_the_same_navigation_feedback_the_arrow_keys_fire()
    {
        // Speech + sonification for a click must come from the standard navigation
        // pipeline — one announcement path for mouse and keyboard alike.
        var h = new Harness();
        h.Click(320, 360);

        var feedback = h.Bus.Log.OfType<FeedbackRequestEvent>().LastOrDefault();
        Assert.NotNull(feedback);
        Assert.Equal(FeedbackType.Navigation, feedback!.Type);
        Assert.True(feedback.IsXMove);
        Assert.True(feedback.IsJump);
    }

    [Fact]
    public void Click_in_the_empty_right_margin_does_not_move_the_cursor()
    {
        // The right margin is reserved future space — there is no bar to hear there.
        var h = new Harness();
        int before = h.Store.State.CurrentDataIndex;

        h.Click(1279, 360); // deep inside the right-margin future slots at the live edge

        Assert.Equal(before, h.Store.State.CurrentDataIndex);
    }

    [Fact]
    public void Drag_past_the_dead_zone_pans_and_does_NOT_select_a_bar()
    {
        var h = new Harness();
        int cursorBefore = h.Store.State.CurrentDataIndex;

        h.Input.ProcessMouse(200, 360, "MouseDown", 1280, 720);
        h.Input.ProcessMouse(700, 360, "MouseMove", 1280, 720);
        h.Input.ProcessMouse(700, 360, "MouseUp", 1280, 720);

        // Pan feedback, not bar-selection feedback.
        Assert.Equal(cursorBefore, h.Store.State.CurrentDataIndex);
        var feedback = h.Bus.Log.OfType<FeedbackRequestEvent>().LastOrDefault();
        Assert.NotNull(feedback);
        Assert.Equal(FeedbackType.ViewportChange, feedback!.Type);
    }

    // ── Chart-level context menu ─────────────────────────────────────────────

    [Fact]
    public void RightClick_on_empty_chart_opens_the_chart_context_menu_with_the_bar_under_the_cursor()
    {
        var h = new Harness();

        h.Input.ProcessMouse(320, 360, "ContextMenu", 1280, 720);

        var ev = h.Bus.Log.OfType<OpenChartContextMenuEvent>().LastOrDefault();
        Assert.NotNull(ev);
        Assert.Equal(320, ev!.ViewportX);
        int expected = ChartMath.MapXToIndex(
            320, 1280, h.Store.State.ViewportStartIndex, h.Store.State.ViewportLength);
        Assert.Equal(expected, ev.BarIndex);
    }

    [Fact]
    public void RightClick_in_the_right_margin_reports_no_bar()
    {
        var h = new Harness();

        h.Input.ProcessMouse(1279, 360, "ContextMenu", 1280, 720);

        var ev = h.Bus.Log.OfType<OpenChartContextMenuEvent>().LastOrDefault();
        Assert.NotNull(ev);
        Assert.Equal(-1, ev!.BarIndex);
    }

    [Fact]
    public void RightClick_works_from_idle_state_with_no_drawing_in_flight()
    {
        // Regression pin: the fast-reject in HandleMouseEvent used to swallow
        // ContextMenu events whenever no drawing flow was active, making right-click
        // dead on an idle chart.
        var h = new Harness();
        h.Input.ProcessMouse(640, 360, "ContextMenu", 1280, 720);
        Assert.Contains(h.Bus.Log, e => e is OpenChartContextMenuEvent);
    }

    // ── Shift+wheel pan + double-click jump-to-live (GlobalInputService) ─────

    [Fact]
    public void WheelPan_backward_pans_the_viewport_toward_older_bars()
    {
        var h = new Harness();
        var global = new GlobalInputService(h.Input, h.Bus, h.Store);
        int startBefore = h.Store.State.ViewportStartIndex;
        Assert.True(startBefore > 0, "Need headroom to pan back from the live edge.");

        global.OnWheelPan(-1);

        Assert.True(h.Store.State.ViewportStartIndex < startBefore);
    }

    [Fact]
    public void DoubleClick_jumps_to_the_live_edge_with_navigation_feedback()
    {
        var h = new Harness();
        var global = new GlobalInputService(h.Input, h.Bus, h.Store);
        // Park the cursor away from the live edge first.
        h.Store.Dispatch(new SetCursorAction(h.Store.State.ViewportStartIndex));
        Assert.NotEqual(h.Bars.Count - 1, h.Store.State.CurrentDataIndex);

        global.OnDoubleClick();

        Assert.Equal(h.Bars.Count - 1, h.Store.State.CurrentDataIndex);
        var feedback = h.Bus.Log.OfType<FeedbackRequestEvent>().LastOrDefault();
        Assert.NotNull(feedback);
        Assert.Equal(FeedbackType.Navigation, feedback!.Type);
        Assert.True(feedback.IsJump);
    }

    // ── Hover crosshair tracker ──────────────────────────────────────────────

    [Fact]
    public void MouseMove_produces_a_hover_sample_with_date_price_and_ohlc_text()
    {
        var h = new Harness();
        using var tracker = new ChartHoverTracker(h.Input, h.Store);

        h.Input.ProcessMouse(320, 360, "MouseMove", 1280, 720);

        Assert.NotNull(tracker.Current);
        Assert.False(string.IsNullOrEmpty(tracker.Current!.DateText));
        Assert.False(string.IsNullOrEmpty(tracker.Current.PriceText));
        Assert.Contains("O ", tracker.Current.OhlcText);
        Assert.Contains("C ", tracker.Current.OhlcText);
    }

    [Fact]
    public void MouseLeave_clears_the_hover_sample()
    {
        var h = new Harness();
        using var tracker = new ChartHoverTracker(h.Input, h.Store);
        h.Input.ProcessMouse(320, 360, "MouseMove", 1280, 720);
        Assert.NotNull(tracker.Current);

        h.Input.ProcessMouse(-1, -1, "MouseLeave", 1280, 720);

        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Hover_in_the_right_margin_shows_nothing()
    {
        var h = new Harness();
        using var tracker = new ChartHoverTracker(h.Input, h.Store);

        h.Input.ProcessMouse(1279, 360, "MouseMove", 1280, 720);

        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Toggle_off_disables_tracking_until_toggled_back_on()
    {
        var h = new Harness();
        using var tracker = new ChartHoverTracker(h.Input, h.Store);

        tracker.Toggle();
        Assert.False(tracker.IsEnabled);
        h.Input.ProcessMouse(320, 360, "MouseMove", 1280, 720);
        Assert.Null(tracker.Current);

        tracker.Toggle();
        h.Input.ProcessMouse(320, 360, "MouseMove", 1280, 720);
        Assert.NotNull(tracker.Current);
    }

    [Fact]
    public void BarDate_formatting_drops_midnight_time_but_keeps_intraday_time()
    {
        Assert.Equal("Mar 5, 2026",
            ChartHoverTracker.FormatBarDate(new System.DateTime(2026, 3, 5, 0, 0, 0)));
        Assert.Equal("Mar 5, 2026 14:30",
            ChartHoverTracker.FormatBarDate(new System.DateTime(2026, 3, 5, 14, 30, 0)));
    }
}
