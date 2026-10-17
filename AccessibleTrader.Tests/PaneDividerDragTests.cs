using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.WebHost;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A dragged pane divider follows the pointer</b> (Cody, 2026-10-17). ChartArea turned the
/// pointer's travel into a pane ratio by dividing by a hard-coded 600 px "estimate" of the chart's
/// height that nothing ever updated, and added it to a stored ratio that started from a guessed
/// 0.15. At 1000x610 the chart is about 330 px tall, so the divider moved about half as far as the
/// pointer; on a tall window it ran ahead. The drag now starts from the pane's height as drawn
/// and the chart element's measured height, and gives the pane exactly the pixels moved.
/// </summary>
public sealed class PaneDividerDragTests
{
    /// <summary>
    /// The math end to end through the renderer's own allocation: a 40 px drag down moves the
    /// divider 40 px down, at two canvas heights.
    /// </summary>
    [Theory]
    [InlineData(600)]
    [InlineData(900)]
    public void A_drag_moves_the_divider_by_exactly_the_pixels_the_pointer_moved(int height)
    {
        const float axisH = 30f;
        float total = height - axisH;
        var names = new[] { "Pane_A", "Pane_B" };
        float DividerAboveB(IReadOnlyDictionary<string, float>? ratios)
        {
            var a = ChartRenderer.AllocatePaneHeights(total, names, ratios, 1f);
            return a.MainHeight + a.IndicatorHeights[0];
        }
        float before = DividerAboveB(null);
        var dividers = new List<(string, float)> { ("Pane_A", 0f), ("Pane_B", before / height) };
        var first = ChartRenderer.AllocatePaneHeights(total, names, null, 1f);
        dividers[0] = ("Pane_A", first.MainHeight / height);

        float start = ChartMath.RenderedPaneRatio("Pane_B", dividers, axisH / height)!.Value;
        float target = ChartMath.DraggedPaneRatio(start, 200, 240, height, axisH / height);
        float after = DividerAboveB(new Dictionary<string, float> { ["Pane_B"] = target });

        Assert.Equal(40f, after - before, 1);
    }

    private const double W = 1000;

    private static (Blazor.BlazorTestHarness Harness, IRenderedComponent<ChartArea> Cut) Rig(double height)
    {
        var bars = Enumerable.Range(0, 50).Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), 10, 11, 9, 10, 1)).ToList();
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ViewportLength = 50 };
        var harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(isBrowserHost: false, state: state);
        var layout = harness.Ctx.Services.GetRequiredService<IPaneLayoutService>();
        // Price pane 0–60%, the RSI pane 60–90%, the x-axis strip the last 10%.
        layout.Dividers.Returns(new List<(string BelowPaneName, float DividerFraction)> { ("Pane_RSI", 0.6f) });
        layout.AxisHeightFraction.Returns(0.1f);
        // The chart element's measured size, as canvasRegion.js reports it.
        harness.Ctx.Services.GetRequiredService<ChartHoverTracker>().OnChartGeometryChanged(0, 0, W, height);
        return (harness, harness.Ctx.RenderComponent<ChartArea>());
    }

    /// <summary>
    /// ChartArea uses the chart's MEASURED height: a 45 px drag down on the RSI pane's divider
    /// takes 45 px off a pane that had a third of the plot, on a 600 px and a 900 px chart.
    /// </summary>
    [Theory]
    [InlineData(600)]
    [InlineData(900)]
    public void ChartArea_converts_the_drag_with_the_charts_measured_height(double height)
    {
        var (harness, cut) = Rig(height);
        using var _ = harness;
        double plot = height * 0.9;
        float expected = (float)(1.0 / 3.0 - 45 / plot);

        cut.Find(".pane-divider").MouseDown(new MouseEventArgs { ClientY = 300 });
        cut.Find("#chart-interact-zone").MouseMove(new MouseEventArgs { ClientY = 345 });

        harness.WorkspaceStore.Received().Dispatch(Arg.Is<SetPaneHeightRatiosAction>(a =>
            Math.Abs(a.Ratios["Pane_RSI"] - expected) < 1e-3));
    }
}
