using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The price pane's symbol line on a Line or Area chart: just <c>C</c>, the raw close, in the
/// line's colour. There is no O/H/L on the canvas, so printing four labels would name numbers
/// the user cannot see and the arrow keys no longer read. Candles and Heikin-Ashi are unchanged.
/// </summary>
public sealed class ChartTypeStatusLineTests
{
    private static readonly ChartIdentity Btc = new("Spot", "Bitstamp", "BTCUSDT", "1d");

    private static ChartTheme Theme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    private static List<Ohlcv> Bars(int n = 40)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n).Select(i =>
        {
            double o = 80_000 + i * 100, c = i % 2 == 0 ? o + 50 : o - 50;
            return new Ohlcv(start.AddDays(i), o, Math.Max(o, c) + 25, Math.Min(o, c) - 25, c, 1000 + i);
        }).ToList();
    }

    private static PaneStatus Build(IReadOnlyList<Ohlcv> bars, int bar, PriceChartStyle style)
    {
        var theme = Theme();
        var ctx = new ChartStatusLine.Context(theme, ChartStatusLine.DefaultBackingAlpha, bars, Btc,
            IsHeikinAshi: false, ChartStyle: style);
        return ChartStatusLine.Build("Main", Array.Empty<ChartSeries>(), bar, ctx, 400f, 4000f,
            ChartStatusLine.LinePx, t => t.Length * 7f);
    }

    [Fact]
    public void On_a_line_chart_the_symbol_line_is_the_close_alone_in_the_line_colour()
    {
        var bars = Bars();
        var line = Build(bars, 20, PriceChartStyle.Line).Lines[0];

        Assert.Equal(new[] { "C" }, line.Values.Select(v => v.Label));
        Assert.Equal(ChartStatusLine.FormatReading(bars[20].Close), line.Values[0].Text);
        Assert.Equal(Theme().PriceChartLine.WithAlpha(255), line.Values[0].Color);
    }

    [Fact]
    public void On_an_area_chart_the_symbol_line_is_the_close_alone_too()
    {
        var line = Build(Bars(), 20, PriceChartStyle.Area).Lines[0];

        Assert.Equal(new[] { "C" }, line.Values.Select(v => v.Label));
    }

    [Fact]
    public void Candles_still_print_open_high_low_and_close()
    {
        var line = Build(Bars(), 20, PriceChartStyle.Candles).Lines[0];

        Assert.Equal(new[] { "O", "H", "L", "C" }, line.Values.Select(v => v.Label));
    }

    [Fact]
    public void An_analytics_tab_with_no_candles_keeps_its_Open_High_Low_Close_line()
    {
        var bars = Bars();
        var config = new SeriesConfig { Id = "price", Name = "Price", Pane = "Main" };
        var state = WorkspaceState.Initial with
        {
            Identity = Btc,
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(
                new ChartSeries(config, new SeriesDataBuffer { SeriesId = "price" })),
            ChartStyle = PriceChartStyle.Line,
            ViewportLength = bars.Count,
        };

        var panes = ChartStatusLine.BuildPanes(state,
            Array.Empty<(string BelowPaneName, float DividerFraction)>(),
            axisHeightFraction: 0.1f, axisWidthFraction: 0.1f, absIndex: 20,
            backingAlpha: ChartStatusLine.DefaultBackingAlpha, theme: Theme(),
            cssWidth: 4000, cssHeight: 400, textScale: 1);

        var line = panes.Single(p => p.Status.PaneName == "Main").Status.Lines[0];
        Assert.Equal(new[] { "O", "H", "L", "C" }, line.Values.Select(v => v.Label));
    }
}
