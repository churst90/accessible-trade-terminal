using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The chart context menu's ONE definition of "here": bar, value and pane, resolved from the
/// pointer or from the keyboard. Mouse origin must use the clicked pane's own scale (an RSI
/// pane is 0–100, not a price); keyboard origin must use the focused series' value there.
/// </summary>
public sealed class ChartContextMenuHereTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Bars(int n = 10) =>
        Enumerable.Range(0, n)
            .Select(i => new Ohlcv(T0.AddHours(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000 + i))
            .ToList();

    private static ChartSeries Rsi(params double[] values)
    {
        var config = new SeriesConfig
        {
            Id = "rsi-1", Name = "RSI 14", FriendlyName = "RSI 14",
            IndicatorCode = "RSI", Pane = "Pane_Rsi",
        };
        config.Components.Add(new ComponentConfig { Name = "RSI" });
        var buffer = new SeriesDataBuffer { SeriesId = "rsi-1" };
        buffer.ComponentData["RSI"] = values;
        return new ChartSeries(config, buffer);
    }

    private static ChartSeries Candles()
    {
        var config = new SeriesConfig
        {
            Id = "candles", Name = "Candles", FriendlyName = "Candles",
            IndicatorCode = "CANDLES", Pane = "Main",
        };
        config.Components.Add(new ComponentConfig { Name = "body" });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" });
    }

    private static WorkspaceState State(ChartSeries focused, string? focusId)
    {
        var bars = Bars();
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(Candles(), focused),
            FocusedSeriesId = focusId,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 3,
            ViewportRange = (100, 200),
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty
                .Add("Pane_Rsi", (0, 100)),
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
        };
    }

    private static readonly (string BelowPaneName, float DividerFraction)[] Dividers =
        { ("Pane_Rsi", 0.5f) };

    [Fact]
    public void Mouse_in_the_price_pane_uses_the_price_scale()
    {
        var state = State(Rsi(50, 51, 52, 55), "rsi-1");

        var here = ChartContextHere.FromMouse(
            state, barIndex: 2, y: 200, canvasHeight: 600,
            Dividers, axisHeightFraction: 0.1f, statusBands: null);

        Assert.Equal("Main", here.Pane);
        Assert.Equal(2, here.BarIndex);
        Assert.Equal(ChartMath.MapYToPrice(200, 300, 100, 200, false), here.Price, 9);
    }

    [Fact]
    public void Mouse_in_an_indicator_pane_uses_that_panes_range_and_names_its_series()
    {
        var state = State(Rsi(50, 51, 52, 55), "rsi-1");

        var here = ChartContextHere.FromMouse(
            state, barIndex: 2, y: 420, canvasHeight: 600,
            Dividers, axisHeightFraction: 0.1f, statusBands: null);

        Assert.Equal("Pane_Rsi", here.Pane);
        Assert.Equal("rsi-1", here.SeriesId);
        Assert.Equal(50, here.Price, 6);   // half way down a 0–100 pane
    }

    [Fact]
    public void Mouse_over_the_x_axis_strip_has_no_pane_or_value()
    {
        var state = State(Rsi(50), "rsi-1");

        var here = ChartContextHere.FromMouse(
            state, barIndex: 2, y: 580, canvasHeight: 600,
            Dividers, axisHeightFraction: 0.1f, statusBands: null);

        Assert.False(here.HasPrice);
    }

    [Fact]
    public void Mouse_in_the_empty_right_margin_has_no_bar()
    {
        var state = State(Rsi(50), "rsi-1");

        var here = ChartContextHere.FromMouse(
            state, barIndex: -1, y: 200, canvasHeight: 600,
            Dividers, axisHeightFraction: 0.1f, statusBands: null);

        Assert.False(here.HasBar);
        Assert.False(here.HasPrice);
    }

    [Fact]
    public void Keyboard_uses_the_focused_indicator_value_on_the_cursor_bar_in_its_own_pane()
    {
        var state = State(Rsi(40, 45, 50, 55, 60), "rsi-1");

        var here = ChartContextHere.FromKeyboard(state);

        Assert.Equal(3, here.BarIndex);
        Assert.Equal("Pane_Rsi", here.Pane);
        Assert.Equal("rsi-1", here.SeriesId);
        Assert.Equal(55, here.Price);
    }

    [Fact]
    public void Keyboard_with_no_focused_series_uses_the_bar_close_on_the_price_pane()
    {
        var state = State(Rsi(40, 45, 50, 55), focusId: null);

        var here = ChartContextHere.FromKeyboard(state);

        Assert.Equal("Main", here.Pane);
        Assert.Equal(103.5, here.Price);   // Bars()[3].Close
    }

    [Fact]
    public void Keyboard_with_the_price_series_focused_uses_the_bar_close_not_an_indicator_value()
    {
        var state = State(Rsi(40, 45, 50, 55), focusId: "candles");

        var here = ChartContextHere.FromKeyboard(state);

        Assert.Equal("Main", here.Pane);
        Assert.Equal(103.5, here.Price);
    }

    [Fact]
    public void Keyboard_focused_component_with_no_value_at_the_bar_has_no_price()
    {
        var state = State(Rsi(40, 45, 50, double.NaN), "rsi-1");

        var here = ChartContextHere.FromKeyboard(state);

        Assert.Equal("Pane_Rsi", here.Pane);
        Assert.False(here.HasPrice);
    }
}
