using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// How a Percent or Indexed chart is READ: the candles sentence starts with the scale reading
/// of the close as drawn, and a main-pane <c>{value:price}</c> reads the scale first and the
/// price second. Indicator panes, bar detail and typed prices keep prices. Regular is the
/// control: not one byte moves.
/// </summary>
public sealed class PriceScaleSpeechTests
{
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1d");

    private static Ohlcv Bar(int i, double close) =>
        new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
            close - 1, close + 3, close - 3, close, 1000 + i);

    private static ChartSeries CandleSeries()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayName = "Body", IsVisible = true,
            DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, DataMapping = "close",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "upper_wick", DisplayName = "Upper Wick", IsVisible = true,
            DisplayType = ComponentDisplayType.Wick, Role = ComponentRole.Wick, DataMapping = "high",
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries ValueSeries(string id, string name, string pane, string indicatorCode,
        string component, string template, params double[] values)
    {
        var config = new SeriesConfig
        {
            Id = id, Name = name, Pane = pane, IndicatorCode = indicatorCode,
        };
        config.Components.Add(new ComponentConfig
        {
            Name = component, DisplayName = name, IsVisible = true,
            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.PriceAction,
            SpeechTemplate = template,
        });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData[component] = values;
        return new ChartSeries(config, buffer);
    }

    private static WorkspaceState State(PriceScaleMode mode, ChartSeries series, int index,
        params double[] closes)
    {
        var bars = closes.Select((c, i) => Bar(i, c)).ToList();
        return WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = index,
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
            ScaleMode = mode,
            ChartStyle = series.Id == "candles" ? PriceChartStyle.Candles : PriceChartStyle.Candles,
            InitStatus = InitializationStatus.Ready,
            LastInteractionContext = InteractionContext.Series,
            SpeakTimestamps = false,
            TimestampReadLocation = "None",
            ReadColumnHeaders = false,
            SpeechOrder = "ValueOnly",
        };
    }

    private static string Speak(WorkspaceState state)
    {
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        Assert.NotEmpty(spy.SpokenTexts);
        return string.Join(" ", spy.SpokenTexts);
    }

    // ── The candles series ────────────────────────────────────────────────

    [Fact]
    public void Percent_candles_start_with_plus_percent_and_keep_the_readout()
    {
        string spoken = Speak(State(PriceScaleMode.Percent, CandleSeries(), 1, 100, 104.2));

        Assert.Contains("Plus 4.20 percent. ", spoken);
        Assert.Contains("Close 104.20", spoken);
        Assert.Contains("Body", spoken);   // the rest of the sentence is unchanged
    }

    [Fact]
    public void Indexed_candles_start_with_the_index_reading()
    {
        string spoken = Speak(State(PriceScaleMode.IndexedTo100, CandleSeries(), 1, 100, 104.2, 110, 95));

        Assert.Contains("Index 104.20. ", spoken);
    }

    [Fact]
    public void A_fall_reads_minus_and_zero_reads_zero()
    {
        Assert.Contains("Minus 1.30 percent. ",
            Speak(State(PriceScaleMode.Percent, CandleSeries(), 1, 100, 98.7)));
        Assert.Contains("Zero percent. ",
            Speak(State(PriceScaleMode.Percent, CandleSeries(), 0, 100, 104.2)));
    }

    [Fact]
    public void Percent_line_charts_get_the_prefix_too()
    {
        var state = State(PriceScaleMode.Percent, CandleSeries(), 1, 100, 104.2)
            with { ChartStyle = PriceChartStyle.Line };

        string spoken = Speak(state);

        Assert.Contains("Plus 4.20 percent. Close 104.20. Up 4.20 from the previous close, 4.20 percent.", spoken);
    }

    [Fact]
    public void Heikin_Ashi_reads_the_HA_close_as_drawn()
    {
        var state = State(PriceScaleMode.Percent, CandleSeries(), 2, 100, 110, 120)
            with { IsHeikinAshi = true };
        var ha = ChartMath.CalculateHeikinAshi(state.Data!.ToList());
        string expected = "Plus " + ((ha[2].Close / ha[0].Close - 1) * 100)
            .ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " percent. ";

        Assert.Contains(expected, Speak(state));
    }

    [Fact]
    public void Regular_candles_are_byte_identical_to_before()
    {
        string spoken = Speak(State(PriceScaleMode.Regular, CandleSeries(), 1, 100, 104.2));

        Assert.Contains("Close 104.20", spoken);
        Assert.DoesNotContain("Plus", spoken);
        Assert.DoesNotContain("Minus", spoken);
        Assert.DoesNotContain("Index", spoken);
    }

    // ── {value:price} on a main-pane series ───────────────────────────────

    private static WorkspaceState TemplateState(PriceScaleMode mode, ChartSeries series, int index,
        params double[] closes)
    {
        var state = State(mode, series, index, closes);
        return state with
        {
            ReadColumnHeaders = true,
            SpeechOrder = "NameValue",
        };
    }

    [Fact]
    public void A_main_pane_price_token_reads_the_scale_then_the_price()
    {
        var overlay = ValueSeries("ma", "Average", "Main", "SMA", "line",
            "{name}. {value:price}.", 107.5, 107.5, 107.5);

        string spoken = Speak(TemplateState(PriceScaleMode.Percent, overlay, 2, 100, 103, 107.5));

        Assert.Contains("plus 7.50 percent, 107.50", spoken);
    }

    [Fact]
    public void An_indexed_main_pane_token_reads_the_index_then_the_price()
    {
        var overlay = ValueSeries("ma", "Average", "Main", "SMA", "line",
            "{name}. {value:price}.", 107.5, 107.5, 107.5);

        string spoken = Speak(TemplateState(PriceScaleMode.IndexedTo100, overlay, 2, 100, 103, 107.5));

        Assert.Contains("index 107.50, 107.50", spoken);
    }

    [Fact]
    public void The_COMPARE_overlay_reads_against_its_own_first_visible_value()
    {
        // The chart base is 100; the comparison starts at 200 and reaches 240. Its own reading
        // is +20 percent, not +140, so the user hears each symbol's change from the left edge.
        var compare = ValueSeries("compare", "Compare symbol", "Main", "COMPARE", "Compare",
            "{name}. {value:price}.", 200, 220, 240);

        string spoken = Speak(TemplateState(PriceScaleMode.Percent, compare, 2, 100, 110, 120));

        Assert.Contains("plus 20.00 percent, 240.00", spoken);
        Assert.DoesNotContain("plus 140", spoken);
    }

    [Fact]
    public void An_indicator_pane_keeps_its_price_reading()
    {
        var rsi = ValueSeries("rsi", "RSI", "Pane_RSI", "RSI", "rsi",
            "{name}. {value:price}.", 70, 70, 70);

        string spoken = Speak(TemplateState(PriceScaleMode.Percent, rsi, 2, 100, 110, 120));

        Assert.Contains("RSI. 70.00.", spoken);
        Assert.DoesNotContain("percent", spoken);
        Assert.DoesNotContain("index", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_regular_chart_keeps_the_plain_price_token()
    {
        var overlay = ValueSeries("ma", "Average", "Main", "SMA", "line",
            "{name}. {value:price}.", 107.5, 107.5, 107.5);

        string spoken = Speak(TemplateState(PriceScaleMode.Regular, overlay, 2, 100, 103, 107.5));

        Assert.Contains("Average. 107.50.", spoken);
        Assert.DoesNotContain("percent", spoken);
    }
}
