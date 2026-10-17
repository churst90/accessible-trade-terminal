using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.Tests.WebHost;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The hover readout under a mouse that does not move.
///
/// <para>Reported 2026-10-15 from a screenshot (BTCUSDT 1d, focus mode): the white hairline sat on
/// about March 31 while the readout box said April 20 and showed April 20's candle, about twenty
/// bars to the right. Cody drives the chart from the keyboard with the mouse parked over it, so
/// the chart pans, zooms, steps, gains bars and resizes under a pointer that never moves.
/// <see cref="ChartHoverTracker"/> computed its sample only on a mouse event, so it went on
/// describing the bar that WAS under the pointer in an older viewport.</para>
///
/// <para>The oracle in these tests is the tracker itself fed a fresh mouse event at the same
/// spot: whatever a still pointer reads after the chart changes must be exactly what the same
/// pointer reads once it is nudged by zero pixels. Every case also asserts the change actually
/// moved the readout, so a no-op change cannot pass vacuously.</para>
/// </summary>
public sealed class ChartHoverStillMouseTests
{
    private const double W = 1280, H = 720, X = 320, Y = 200;

    private sealed class Harness
    {
        public WorkspaceStore Store { get; }
        public BlazorInputService Input { get; } = new();
        public SpyEventBus Bus { get; } = new();
        public PaneLayoutService PaneLayout { get; } = new();
        public List<Ohlcv> Bars { get; } = new();

        public Harness(int barCount = 200)
        {
            Store = new WorkspaceStore(Bus, new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());
            // A rising series with a wide range, so a pan changes the date, a log-scale toggle
            // changes the price under a fixed Y, and every bar's OHLC is distinct.
            for (int i = 0; i < barCount; i++)
            {
                double c = 100 + i * 2;
                Bars.Add(new Ohlcv(
                    new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc).AddDays(i),
                    c - 1, c + 3, c - 3, c, 1000));
            }
            Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Bars), IsInitialLoad: true));
        }

        public ChartHoverTracker Tracker(ISonificationManager? sonify = null, ISettingsManager? settings = null,
            ISpeechFeedbackRouter? speech = null)
            => new(Input, Store, sonify, settings, speech, PaneLayout, Bus);

        public void Move(string type = "MouseMove") => Input.ProcessMouse(X, Y, type, W, H);
    }

    /// <summary>What the tracker says once the still pointer is "moved" onto the same spot.</summary>
    private static ChartHoverInfo? FreshSample(Harness h, ChartHoverTracker t)
    {
        h.Move();
        return t.Current;
    }

    public static TheoryData<string> MappingChanges => new()
    {
        "pan", "zoom", "log scale", "new bar",
        // "live tick on the hovered bar" was a case here until 2026-10-17: it rewrote the hovered
        // bar's OHLC, which the readout box printed. The box is retired (Cody's decision:
        // TradingView status line); the hover sample now names only the bar, its date and the
        // value at the pointer, none of which a tick changes. The O H L C is the price pane's status
        // line, rebuilt from the store on every render — pinned by
        // StatusLineInChartAreaTests.A_live_tick_on_the_hovered_bar_reaches_the_status_line.
    };

    private static void Apply(Harness h, string change, ChartHoverInfo hovered)
    {
        switch (change)
        {
            case "pan":
                h.Store.Dispatch(new PanAction(-20));
                break;
            case "zoom":
                h.Store.Dispatch(new ZoomAction(h.Store.State.ViewportLength / 2));
                break;
            case "log scale":
                h.Store.Dispatch(new ToggleLogScaleAction());
                break;
            case "new bar":
                var last = h.Store.State.Data[h.Store.State.Data.Count - 1];
                h.Store.Dispatch(new UpdateDataAction(
                    h.Store.State.Data.Append(new Ohlcv(last.Date.AddDays(1), 600, 650, 590, 640, 1000)),
                    IsInitialLoad: false));
                break;
            default:
                throw new System.ArgumentException(change);
        }
    }

    [Theory]
    [MemberData(nameof(MappingChanges))]
    public void A_still_mouse_reads_the_bar_now_under_it_after_the_chart_changes(string change)
    {
        var h = new Harness();
        using var t = h.Tracker();
        h.Move();
        var before = t.Current;
        Assert.NotNull(before);

        Apply(h, change, before!);
        var still = t.Current;

        var fresh = FreshSample(h, t);
        Assert.NotNull(fresh);
        // Vacuity: the change must have changed what this spot reads, or the test proves nothing.
        Assert.NotEqual(before, fresh);
        Assert.True(Equals(fresh, still),
            $"After '{change}' with the mouse held still the readout said " +
            $"'{still?.DateText} {still?.PriceText} / bar {still?.BarIndex}' (hairline x {still?.BarX:F1}), " +
            $"but the bar now under that same pointer is " +
            $"'{fresh!.DateText} {fresh.PriceText} / bar {fresh.BarIndex}' (hairline x {fresh.BarX:F1}). " +
            $"Before the change it said '{before.DateText} / bar {before.BarIndex}'.");
    }

    [Fact]
    public void A_pane_divider_moving_under_a_still_mouse_updates_the_price()
    {
        // A volume or oscillator pane added or removed moves the main pane's bottom edge, so the
        // price under a fixed Y changes while the canvas size does not. Recompute, don't clear.
        var h = new Harness();
        using var t = h.Tracker();
        h.Move();
        var before = t.Current;
        Assert.NotNull(before);

        h.PaneLayout.Update(new[] { ("Volume", 0.5f) }, 0.05f, 0.05f);
        var still = t.Current;

        var fresh = FreshSample(h, t);
        Assert.NotEqual(before!.PriceText, fresh!.PriceText);
        Assert.Equal(fresh, still);
    }

    // ── A re-read is silent ──────────────────────────────────────────────────

    [Theory]
    [InlineData("MouseMove")]
    [InlineData("TouchExplore")]
    public void The_chart_moving_under_a_still_pointer_makes_no_sound_and_no_speech(string type)
    {
        // The hover tick (opt-in, ON here) and explore speech answer the user's own pointer
        // movement. Panning with the arrow keys under a parked mouse already has its own
        // navigation feedback; a second tick or a second voice on top of it would be noise.
        var h = new Harness();
        var sonify = Substitute.For<ISonificationManager>();
        var speech = Substitute.For<ISpeechFeedbackRouter>();
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(ChartHoverTracker.HoverSonificationKey, Arg.Any<Newtonsoft.Json.Linq.JToken?>())
            .Returns(new Newtonsoft.Json.Linq.JValue(true));
        using var t = h.Tracker(sonify, settings, speech);

        h.Move(type);
        // Vacuity: the pointer's own move IS audible in this configuration.
        sonify.Received(1).PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
            Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
        var before = t.Current;
        sonify.ClearReceivedCalls();
        speech.ClearReceivedCalls();

        h.Store.Dispatch(new PanAction(-20));
        h.Store.Dispatch(new ZoomAction(h.Store.State.ViewportLength / 2));
        h.Store.Dispatch(new ToggleLogScaleAction());
        h.PaneLayout.Update(new[] { ("Volume", 0.5f) }, 0.05f, 0.05f);

        // The re-reads happened (or this would pass on the old, frozen readout) ...
        Assert.NotEqual(before, t.Current);
        // ... and none of them made a sound.
        sonify.DidNotReceive().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
            Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
        speech.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<SpeechChannel>());
    }

    // ── The chart moving on screen re-maps ───────────────────────────────────
    //
    // A scroll, a window resize or focus mode moves the CHART, not the pointer. Cody's decision
    // (2026-10-15): the readout follows the pointer's unchanged spot on screen onto the chart's
    // new layout, rather than clearing until the next mouse move. The pointer here starts at
    // element (X, Y) = (320, 200) in a chart whose rect is at (0, 120), so on screen at (320, 320).

    private const double Left0 = 0, Top0 = 120;

    /// <summary>What a fresh mouse event at element position (x, y) of a w×h chart reads.</summary>
    private static ChartHoverInfo? FreshAt(Harness h, ChartHoverTracker t, double x, double y, double w, double hh)
    {
        h.Input.ProcessMouse(x, y, "MouseMove", w, hh);
        return t.Current;
    }

    private static string Describe(ChartHoverInfo? i)
        => i == null ? "nothing" : $"'{i.DateText} {i.PriceText} / bar {i.BarIndex}' (hairline x {i.BarX:F1})";

    [Theory]
    [InlineData("sideways", -200.0, Top0, true)]
    [InlineData("up", Left0, 20.0, false)]
    public void Scrolling_the_chart_under_a_still_mouse_re_maps_the_readout_to_the_spot_now_under_it(
        string direction, double newLeft, double newTop, bool barChanges)
    {
        var h = new Harness();
        using var t = h.Tracker();
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        h.Move();
        var before = t.Current;
        Assert.NotNull(before);

        t.OnChartGeometryChanged(newLeft, newTop, W, H);
        var still = t.Current;

        double nx = Left0 + X - newLeft, ny = Top0 + Y - newTop;
        var fresh = FreshAt(h, t, nx, ny, W, H);
        Assert.NotNull(fresh);
        // Vacuity: the scroll moved the pointer onto a different bar (sideways) or a different
        // price (up), so a readout that did not follow cannot pass.
        if (barChanges) Assert.NotEqual(before!.DateText, fresh!.DateText);
        else Assert.NotEqual(before!.PriceText, fresh!.PriceText);
        Assert.True(Equals(fresh, still),
            $"After scrolling the chart {direction} under a still mouse the readout said {Describe(still)}, " +
            $"but a mouse event at the same spot on screen (element {nx}, {ny}) reads {Describe(fresh)}. " +
            $"Before the scroll it said {Describe(before)}.");
    }

    [Fact]
    public void Resizing_the_chart_under_a_still_mouse_re_maps_the_readout()
    {
        // Narrower and further right (a side panel opened), taller: the same screen point is at
        // a different fraction of the plot, so on a different bar.
        var h = new Harness();
        using var t = h.Tracker();
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        h.Move();
        var before = t.Current;
        Assert.NotNull(before);

        const double newLeft = 100, newTop = 60, newW = W - 300, newH = H + 100;
        t.OnChartGeometryChanged(newLeft, newTop, newW, newH);
        var still = t.Current;

        var fresh = FreshAt(h, t, Left0 + X - newLeft, Top0 + Y - newTop, newW, newH);
        Assert.NotNull(fresh);
        Assert.NotEqual(before!.DateText, fresh!.DateText);
        Assert.True(Equals(fresh, still),
            $"After the resize the still readout said {Describe(still)}; the bar now under the pointer is {Describe(fresh)}.");
    }

    [Fact]
    public void After_a_re_map_later_chart_changes_read_from_the_new_spot()
    {
        // The stored sample moves with the re-map, so a pan after a scroll reads the bar under
        // the pointer's new element position, not the one it had before the scroll.
        var h = new Harness();
        using var t = h.Tracker();
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        h.Move();
        t.OnChartGeometryChanged(-200, Top0, W, H);

        h.Store.Dispatch(new PanAction(-20));
        var still = t.Current;

        var atOldSpot = FreshAt(h, t, X, Y, W, H);
        var fresh = FreshAt(h, t, X + 200, Y, W, H);
        Assert.NotEqual(atOldSpot, fresh); // vacuity: the two spots read differently
        Assert.True(Equals(fresh, still),
            $"A pan after the scroll read {Describe(still)}; the pointer's spot reads {Describe(fresh)} " +
            $"(the pre-scroll element position would read {Describe(atOldSpot)}).");
    }

    public static TheoryData<string, double, double, double, double> PointerOffTheChart => new()
    {
        // name, new left, new top, new width, new height. The pointer is on screen at (320, 320).
        { "scrolled down below the pointer", Left0, 400, W, H },
        { "moved right past the pointer", 400, Top0, W, H },
        { "moved left past the pointer", -1000, Top0, W, H },
        { "scrolled up past the pointer", Left0, -600, W, H },
        { "shrank away from the pointer", Left0, Top0, 200, H },
    };

    [Theory]
    [MemberData(nameof(PointerOffTheChart))]
    public void A_chart_that_moves_out_from_under_a_still_mouse_clears_the_readout(
        string how, double newLeft, double newTop, double newW, double newH)
    {
        var h = new Harness();
        // Panned back from the latest bar, so there ARE bars to the right of the viewport and a
        // missed bounds check would name one of them rather than come out empty by accident.
        h.Store.Dispatch(new PanAction(-60));
        using var t = h.Tracker();
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        h.Move();
        Assert.NotNull(t.Current);

        t.OnChartGeometryChanged(newLeft, newTop, newW, newH);
        Assert.True(t.Current == null,
            $"The chart {how}; the pointer is no longer on it, but the readout still says {Describe(t.Current)}.");

        // The sample is gone, not just hidden: a chart change does not bring it back ...
        h.Store.Dispatch(new PanAction(-5));
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        Assert.Null(t.Current);
        // ... and the next real move does.
        h.Move();
        Assert.NotNull(t.Current);
    }

    [Fact]
    public void Focus_mode_hides_the_readout_until_the_new_rect_arrives_then_re_maps_it()
    {
        // Alt+Z re-lays the chart out; its new rect arrives in a later report. In between, the
        // old mapping must not be shown over the new layout, not even by a chart change.
        var h = new Harness();
        using var t = h.Tracker();
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        h.Move();
        var before = t.Current;
        Assert.NotNull(before);

        h.Bus.Publish(new FocusModeChangedEvent(true));
        Assert.True(t.Current == null, $"Focus mode is laying the chart out again, but the readout still says {Describe(t.Current)}.");
        h.Store.Dispatch(new PanAction(-20));
        Assert.True(t.Current == null, $"A pan before the new rect arrived showed {Describe(t.Current)} from the old layout.");

        const double newTop = 40, newH = H + 200;
        t.OnChartGeometryChanged(Left0, newTop, W, newH);
        var still = t.Current;

        var fresh = FreshAt(h, t, X, Top0 + Y - newTop, W, newH);
        Assert.NotNull(fresh);
        Assert.NotEqual(before, fresh);
        Assert.True(Equals(fresh, still),
            $"Once focus mode's rect arrived the readout said {Describe(still)}; the pointer is on {Describe(fresh)}.");
    }

    [Fact]
    public void Focus_mode_that_leaves_the_chart_rect_unchanged_shows_the_readout_again_on_the_next_report()
    {
        var h = new Harness();
        using var t = h.Tracker();
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        h.Move();
        var before = t.Current;

        h.Bus.Publish(new FocusModeChangedEvent(false));
        Assert.Null(t.Current);
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        Assert.Equal(before, t.Current);
    }

    [Fact]
    public void A_sample_taken_before_any_chart_rect_was_known_is_cleared_rather_than_guessed()
    {
        // Without the rect the pointer was measured against, its spot on screen is unknown, so
        // there is nothing to re-map from.
        var h = new Harness();
        using var t = h.Tracker();
        h.Move();
        Assert.NotNull(t.Current);

        t.OnChartGeometryChanged(Left0, Top0, W, H);
        Assert.True(t.Current == null, $"With no known origin the readout was re-mapped to {Describe(t.Current)}.");
        h.Store.Dispatch(new PanAction(-20));
        Assert.Null(t.Current);

        // Focus mode on such a sample clears it too, and the report that follows does not revive it.
        var h2 = new Harness();
        using var t2 = h2.Tracker();
        h2.Move();
        h2.Bus.Publish(new FocusModeChangedEvent(true));
        Assert.Null(t2.Current);
        t2.OnChartGeometryChanged(Left0, 40, W, H + 200);
        Assert.Null(t2.Current);
    }

    [Fact]
    public void A_sample_whose_size_disagrees_with_the_last_reported_rect_is_cleared_on_the_next_rect()
    {
        // The mouse handler reports a changed rect before the move measured against it, so a
        // move whose size disagrees with the last report came from an older script or a lost
        // report: its origin is unknown, and it is not re-mapped from the wrong one.
        var h = new Harness();
        using var t = h.Tracker();
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        h.Input.ProcessMouse(X, Y, "MouseMove", W - 100, H);
        Assert.NotNull(t.Current);

        t.OnChartGeometryChanged(Left0, Top0, W - 100, H);
        Assert.True(t.Current == null, $"A sample with no trustworthy origin was re-mapped to {Describe(t.Current)}.");
    }

    [Fact]
    public void The_same_rect_reported_again_changes_nothing()
    {
        // canvasRegion.js reports on every scroll anywhere on the page; most do not move the chart.
        var h = new Harness();
        using var t = h.Tracker();
        t.OnChartGeometryChanged(Left0, Top0, W, H);
        h.Move();
        var before = t.Current;
        int raised = 0;
        t.Changed += () => raised++;

        t.OnChartGeometryChanged(Left0, Top0, W, H);
        t.OnChartGeometryChanged(Left0 + 0.2, Top0 - 0.3, W, H); // sub-pixel jitter

        Assert.Equal(0, raised);
        Assert.Same(before, t.Current);
    }

    [Theory]
    [InlineData("MouseMove")]
    [InlineData("TouchExplore")]
    public void A_re_map_after_a_scroll_resize_or_focus_mode_makes_no_sound_and_no_speech(string type)
    {
        var h = new Harness();
        var sonify = Substitute.For<ISonificationManager>();
        var speech = Substitute.For<ISpeechFeedbackRouter>();
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(ChartHoverTracker.HoverSonificationKey, Arg.Any<Newtonsoft.Json.Linq.JToken?>())
            .Returns(new Newtonsoft.Json.Linq.JValue(true));
        using var t = h.Tracker(sonify, settings, speech);
        t.OnChartGeometryChanged(Left0, Top0, W, H);

        h.Move(type);
        // Vacuity: the pointer's own move IS audible in this configuration.
        sonify.Received(1).PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
            Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
        if (type == "TouchExplore")
            speech.Received(1).Speak(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<SpeechChannel>());
        var before = t.Current;
        sonify.ClearReceivedCalls();
        speech.ClearReceivedCalls();

        t.OnChartGeometryChanged(-200, Top0, W, H);                // scroll sideways
        var afterScroll = t.Current;
        t.OnChartGeometryChanged(100, 60, W - 300, H + 100);       // resize
        h.Bus.Publish(new FocusModeChangedEvent(true));
        t.OnChartGeometryChanged(0, 40, W, H + 200);               // focus mode's rect

        // The re-maps happened and moved to another bar ...
        Assert.NotNull(afterScroll);
        Assert.NotEqual(before!.DateText, afterScroll!.DateText);
        Assert.NotNull(t.Current);
        // ... and none of them made a sound.
        sonify.DidNotReceive().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
            Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
        speech.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<SpeechChannel>());
    }

    [Fact]
    public void ChartArea_hands_the_chart_rect_to_the_tracker_and_the_readout_follows_a_scroll()
    {
        // The wire from canvasRegion.js: ChartArea's [JSInvokable] OnChartGeometryChanged.
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 100; i++)
            bars.Add(new Ohlcv(new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc).AddDays(i),
                100 + i, 103 + i, 97 + i, 101 + i, 1000));
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ViewportStartIndex = 0,
            ViewportLength = 100,
            ViewportRange = (90, 210),
        };
        using var harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(
            isBrowserHost: false, state: state);
        var cut = harness.Ctx.RenderComponent<ChartArea>();
        var input = harness.Ctx.Services.GetRequiredService<BlazorInputService>();
        var tracker = harness.Ctx.Services.GetRequiredService<ChartHoverTracker>();

        cut.InvokeAsync(() => cut.Instance.OnChartGeometryChanged(Left0, Top0, W, H)).GetAwaiter().GetResult();
        cut.InvokeAsync(() => input.ProcessMouse(X, Y, "MouseMove", W, H)).GetAwaiter().GetResult();
        var shown = tracker.Current;
        Assert.NotNull(shown);
        cut.WaitForAssertion(() => Assert.Contains(shown!.DateText, cut.Markup));

        // Scrolled sideways by 200px: the pointer is over another bar now, and the markup says so.
        cut.InvokeAsync(() => cut.Instance.OnChartGeometryChanged(-200, Top0, W, H)).GetAwaiter().GetResult();
        var remapped = tracker.Current;
        Assert.NotNull(remapped);
        Assert.NotEqual(shown!.DateText, remapped!.DateText);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(remapped.DateText, cut.Markup);
            Assert.DoesNotContain(shown.DateText, cut.Markup);
        });

        // Scrolled so the chart is below the pointer: nothing is named.
        cut.InvokeAsync(() => cut.Instance.OnChartGeometryChanged(-200, 800, W, H)).GetAwaiter().GetResult();
        Assert.Null(tracker.Current);
        cut.WaitForAssertion(() => Assert.DoesNotContain(remapped.DateText, cut.Markup));
    }

    // ── Throttle and the off switch ──────────────────────────────────────────

    [Fact]
    public void A_dispatch_that_changes_no_mapping_input_raises_no_change()
    {
        // The store dispatches constantly on a live chart; only a change to what the pointer
        // maps through may re-render the readout.
        var h = new Harness();
        using var t = h.Tracker();
        h.Move();
        var before = t.Current;
        int raised = 0;
        t.Changed += () => raised++;

        h.Store.Dispatch(new SetCursorAction(h.Store.State.ViewportStartIndex + 3));

        Assert.Equal(0, raised);
        Assert.Same(before, t.Current);

        // Control: a mapping change on the same harness does raise it.
        h.Store.Dispatch(new PanAction(-20));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_hidden_crosshair_is_not_brought_back_by_a_chart_change()
    {
        var h = new Harness();
        using var t = h.Tracker();
        h.Move();
        t.Toggle(); // off
        Assert.Null(t.Current);

        h.Store.Dispatch(new PanAction(-20));
        Assert.Null(t.Current);

        t.Toggle(); // on again: still nothing until the pointer moves
        h.Store.Dispatch(new PanAction(5));
        Assert.Null(t.Current);
    }
}
