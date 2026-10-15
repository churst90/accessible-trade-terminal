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
        "pan", "zoom", "log scale", "new bar", "live tick on the hovered bar",
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
            case "live tick on the hovered bar":
                // The bar under the pointer is rewritten in place (ReplaceLast on the live bar in
                // production; here whichever bar is hovered, so the readout's OHLC must change).
                var bars = h.Store.State.Data.ToList();
                int idx = bars.FindIndex(b => ChartHoverTracker.FormatBarDate(b.Date, PlaybackNarration.BarSeconds(h.Store.State)) == hovered.DateText);
                Assert.True(idx >= 0, $"no bar is named '{hovered.DateText}'");
                bars[idx] = bars[idx] with { Close = bars[idx].Close + 7.5 };
                h.Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(bars), IsInitialLoad: false));
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
            $"'{still?.DateText} {still?.PriceText} / {still?.OhlcText}' (hairline x {still?.BarX:F1}), " +
            $"but the bar now under that same pointer is " +
            $"'{fresh!.DateText} {fresh.PriceText} / {fresh.OhlcText}' (hairline x {fresh.BarX:F1}). " +
            $"Before the change it said '{before.DateText} / {before.OhlcText}'.");
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

    // ── A size change clears ─────────────────────────────────────────────────

    [Fact]
    public void Resizing_the_chart_under_a_still_mouse_clears_the_readout_until_the_mouse_moves()
    {
        // The pointer's position relative to the element is stale once the element is resized;
        // the readout hides rather than naming a bar the pointer may not be on.
        var h = new Harness();
        using var t = h.Tracker();
        t.OnChartGeometryChanged(0, 120, W, H);     // first report: just recorded
        h.Move();
        Assert.NotNull(t.Current);

        t.OnChartGeometryChanged(0, 120, W, H);     // the same rect again (a scroll elsewhere)
        Assert.NotNull(t.Current);

        t.OnChartGeometryChanged(0, 40, W, H + 200); // focus mode: taller, higher
        Assert.Null(t.Current);

        // ... and a later chart change does not bring the old sample back.
        h.Store.Dispatch(new PanAction(-20));
        Assert.Null(t.Current);

        h.Move();
        Assert.NotNull(t.Current);
    }

    [Fact]
    public void Focus_mode_clears_the_readout_until_the_mouse_moves()
    {
        var h = new Harness();
        using var t = h.Tracker();
        h.Move();
        Assert.NotNull(t.Current);

        h.Bus.Publish(new FocusModeChangedEvent(true));
        Assert.Null(t.Current);
        h.Store.Dispatch(new PanAction(-20));
        Assert.Null(t.Current);
    }

    [Fact]
    public void ChartArea_hands_the_chart_rect_to_the_tracker_and_the_readout_disappears_on_resize()
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

        cut.InvokeAsync(() => cut.Instance.OnChartGeometryChanged(0, 120, W, H)).GetAwaiter().GetResult();
        cut.InvokeAsync(() => input.ProcessMouse(X, Y, "MouseMove", W, H)).GetAwaiter().GetResult();
        var shown = tracker.Current;
        Assert.NotNull(shown);
        cut.WaitForAssertion(() => Assert.Contains(shown!.OhlcText, cut.Markup));

        cut.InvokeAsync(() => cut.Instance.OnChartGeometryChanged(0, 40, W, H + 200)).GetAwaiter().GetResult();

        Assert.Null(tracker.Current);
        cut.WaitForAssertion(() => Assert.DoesNotContain(shown!.OhlcText, cut.Markup));
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
