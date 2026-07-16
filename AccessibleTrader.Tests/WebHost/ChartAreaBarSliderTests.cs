using System.Collections.Generic;
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Phase C: the screen-reader bar-navigator slider in ChartArea — the web
/// analog of the iOS "adjustable" trait. VoiceOver/TalkBack adjust a real
/// range input natively (flick up/down), and each step must route through the
/// SAME NavigateAction pipeline the arrow keys use so the bar is spoken and
/// sonified identically.
/// </summary>
public class ChartAreaBarSliderTests
{
    private static WorkspaceState StateWithBars(int count, int cursor)
    {
        var bars = new List<Ohlcv>();
        for (int i = 0; i < count; i++)
        {
            bars.Add(new Ohlcv(
                new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc).AddMinutes(i),
                100, 101, 99, 100 + i, 1000));
        }
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = cursor,
        };
    }

    [Fact]
    public void Slider_is_absent_on_non_touch_devices_even_with_data()
    {
        // Cody's desktop Orca kept meeting "Bar navigator" in the tab order:
        // the flick slider is a MOBILE affordance and shares the toolbar's
        // touch gate — desktop (no touch) must not render it at all.
        using var harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(
            isBrowserHost: true, state: StateWithBars(count: 50, cursor: 10));
        harness.Ctx.JSInterop.Setup<bool>("accessibleTrader.isTouchCapable").SetResult(false);
        var cut = harness.Ctx.RenderComponent<ChartArea>();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#chart-bar-slider")));
    }

    [Fact]
    public void Slider_is_absent_until_chart_data_loads()
    {
        using var harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(isBrowserHost: true);
        var cut = harness.Ctx.RenderComponent<ChartArea>();

        Assert.Empty(cut.FindAll("#chart-bar-slider"));
    }

    [Fact]
    public void Slider_spans_the_loaded_data_and_reflects_the_cursor()
    {
        using var harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(
            isBrowserHost: true, state: StateWithBars(count: 50, cursor: 10));
        var cut = harness.Ctx.RenderComponent<ChartArea>();

        // The slider appears once the async touch-controls gate resolves.
        cut.WaitForAssertion(() => cut.Find("#chart-bar-slider"));
        var slider = cut.Find("#chart-bar-slider");
        Assert.Equal("0", slider.GetAttribute("min"));
        Assert.Equal("49", slider.GetAttribute("max"));
        Assert.Equal("1", slider.GetAttribute("step")); // per-bar (TalkBack honours step)
        Assert.Equal("10", slider.GetAttribute("value"));
    }

    [Fact]
    public void ValueText_names_the_position_date_and_close_for_the_screen_reader()
    {
        using var harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(
            isBrowserHost: true, state: StateWithBars(count: 50, cursor: 10));
        var cut = harness.Ctx.RenderComponent<ChartArea>();

        cut.WaitForAssertion(() => cut.Find("#chart-bar-slider"));
        var valueText = cut.Find("#chart-bar-slider").GetAttribute("aria-valuetext") ?? "";
        Assert.Contains("Bar 11 of 50", valueText);
        Assert.Contains("close", valueText);
        Assert.Contains("2026", valueText);
    }

    [Fact]
    public void Flicking_the_slider_routes_through_the_arrow_key_navigation_pipeline()
    {
        using var harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(
            isBrowserHost: true, state: StateWithBars(count: 50, cursor: 10));
        AccessibleTrader.Core.Models.FeedbackRequestEvent? feedback = null;
        harness.EventBus.Subscribe<AccessibleTrader.Core.Models.FeedbackRequestEvent>(f => feedback = f);
        var cut = harness.Ctx.RenderComponent<ChartArea>();

        cut.WaitForAssertion(() => cut.Find("#chart-bar-slider"));
        cut.Find("#chart-bar-slider").Input("15");

        // NavigateAction (not SetCursorAction): it scrolls the viewport to keep
        // the target bar visible, exactly like arrow-key navigation.
        harness.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<NavigateAction>(a => a.NewIndex == 15));
        Assert.NotNull(feedback);
        Assert.Equal(AccessibleTrader.Core.Models.FeedbackType.Navigation, feedback!.Type);
        Assert.True(feedback.IsXMove);
    }

    [Fact]
    public void Slider_input_at_the_current_bar_is_a_noop()
    {
        using var harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(
            isBrowserHost: true, state: StateWithBars(count: 50, cursor: 10));
        var cut = harness.Ctx.RenderComponent<ChartArea>();

        cut.WaitForAssertion(() => cut.Find("#chart-bar-slider"));
        cut.Find("#chart-bar-slider").Input("10");

        harness.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<NavigateAction>());
    }
}
