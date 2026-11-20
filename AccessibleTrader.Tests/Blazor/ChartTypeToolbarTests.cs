using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The toolbar's chart-type select. It is one control for the whole choice — Candles, Heikin
/// Ashi, Line, Area — reached by Alt+C, and it is gated on trading tabs
/// exactly as that button is.
/// </summary>
public sealed class ChartTypeToolbarTests
{
    private static WorkspaceState State(PriceChartStyle style = PriceChartStyle.Candles,
        bool heikinAshi = false, ProviderDataShape shape = ProviderDataShape.Ohlcv)
    {
        var config = new SeriesConfig { Id = "candles", Name = "Candles" };
        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            ActiveSeries = ImmutableList.Create(new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" })),
            FocusedSeriesId = "candles",
            ChartStyle = style,
            IsHeikinAshi = heikinAshi,
            CurrentDataShape = shape,
        };
    }

    [Fact]
    public void The_select_has_a_visually_hidden_label_and_shows_the_current_choice()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceChartStyle.Line));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Equal("Chart type", cut.Find("label[for=chart-type-select]").TextContent.Trim());
        Assert.Contains("visually-hidden", cut.Find("label[for=chart-type-select]").ClassList);
        Assert.Equal("line", cut.Find("#chart-type-select").GetAttribute("value"));
    }

    [Fact]
    public void Heikin_Ashi_shows_as_its_own_choice()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(heikinAshi: true));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Equal("heikin", cut.Find("#chart-type-select").GetAttribute("value"));
    }

    [Fact]
    public void Changing_it_dispatches_that_chart_type()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State());

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#chart-type-select").Change("area");

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetChartStyleAction>(a => a.Style == PriceChartStyle.Area && !a.HeikinAshi));
    }

    [Theory]
    [InlineData("bars", PriceChartStyle.Bars, "Bars")]
    [InlineData("hollow", PriceChartStyle.HollowCandles, "Hollow candles")]
    [InlineData("volumecandles", PriceChartStyle.VolumeCandles, "Volume candles")]
    [InlineData("baseline", PriceChartStyle.Baseline, "Baseline")]
    [InlineData("highlow", PriceChartStyle.HighLow, "High-low")]
    [InlineData("hlcarea", PriceChartStyle.HlcArea, "HLC area")]
    public void Each_more_chart_type_is_a_choice_that_dispatches_and_shows_itself(
        string value, PriceChartStyle style, string label)
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State());
        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Equal(label, cut.FindAll("#chart-type-select option").Single(o => o.GetAttribute("value") == value).TextContent.Trim());
        cut.Find("#chart-type-select").Change(value);
        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetChartStyleAction>(a => a.Style == style && !a.HeikinAshi));

        using var shown = new BlazorTestHarness();
        shown.WorkspaceStore.State.Returns(_ => State(style));
        Assert.Equal(value, shown.Ctx.RenderComponent<Cmp.Toolbar>().Find("#chart-type-select").GetAttribute("value"));
    }

    [Fact]
    public void Choosing_Heikin_Ashi_dispatches_candles_plus_HA()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(PriceChartStyle.Line));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#chart-type-select").Change("heikin");

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetChartStyleAction>(a => a.Style == PriceChartStyle.Candles && a.HeikinAshi));
    }

    [Fact]
    public void It_is_absent_on_an_analytics_chart_like_the_Heikin_Ashi_button()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => State(shape: ProviderDataShape.SingleValueLine));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        Assert.Empty(cut.FindAll("#chart-type-select"));
        Assert.Empty(cut.FindAll("label[for=chart-type-select]"));
    }
}
