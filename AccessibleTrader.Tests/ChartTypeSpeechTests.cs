using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The arrow-key reading on the candles series when the price is drawn as a Line or an Area.
/// There is no candle on screen — no body, no wicks, no shape — so the sentence describes the
/// point on the line: the raw close and its move from the previous close. Candles and
/// Heikin-Ashi keep the full candle anatomy, byte for byte.
/// </summary>
public sealed class ChartTypeSpeechTests
{
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1d");

    private static ChartSeries CandleSeries()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        config.Components.Add(new ComponentConfig
        {
            Name = "upper_wick", DisplayName = "Upper Wick", IsVisible = true,
            DisplayType = ComponentDisplayType.Wick, Role = ComponentRole.Wick, DataMapping = "high",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayName = "Body", IsVisible = true,
            DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, DataMapping = "close",
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "lower_wick", DisplayName = "Lower Wick", IsVisible = true,
            DisplayType = ComponentDisplayType.Wick, Role = ComponentRole.Wick, DataMapping = "low",
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static WorkspaceState State(PriceChartStyle style, int index, params double[] closes)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = closes.Select((c, i) =>
            new Ohlcv(start.AddDays(i), c - 2, c + 3, c - 3, c, 1000 + i)).ToList();
        var series = CandleSeries();
        return WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 1,
            CurrentDataIndex = index,
            ChartStyle = style,
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

    private static string Price(double v) => SpeechPriceFormatter.FormatPrice(v);

    [Fact]
    public void On_line_an_up_bar_reads_the_close_and_the_rise()
    {
        string spoken = Speak(State(PriceChartStyle.Line, 1, 100, 110));

        Assert.Contains($"Close {Price(110)}. Up {Price(10)} from the previous close, 10.00 percent.", spoken);
        Assert.DoesNotContain("Body", spoken);
        Assert.DoesNotContain("wick", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void On_line_a_down_bar_reads_the_close_and_the_fall()
    {
        string spoken = Speak(State(PriceChartStyle.Line, 2, 100, 110, 99));

        Assert.Contains($"Close {Price(99)}. Down {Price(11)} from the previous close, 10.00 percent.", spoken);
    }

    [Fact]
    public void On_line_an_unchanged_bar_says_so()
    {
        string spoken = Speak(State(PriceChartStyle.Area, 2, 100, 110, 110));

        Assert.Contains($"Close {Price(110)}. Unchanged from the previous close.", spoken);
    }

    [Fact]
    public void On_line_the_first_bar_says_first_bar()
    {
        string spoken = Speak(State(PriceChartStyle.Line, 0, 123.5, 130));

        Assert.Contains($"Close {Price(123.5)}. First bar.", spoken);
    }

    [Fact]
    public void Candles_still_read_the_full_candle_anatomy()
    {
        string spoken = Speak(State(PriceChartStyle.Candles, 1, 100, 110));

        Assert.Contains("Close", spoken);
        Assert.Contains("Open", spoken);
        Assert.Contains("High", spoken);
        Assert.Contains("Low", spoken);
        Assert.Contains("Body", spoken);
        Assert.Contains("Upper wick", spoken);
        Assert.Contains("Lower wick", spoken);
    }

    [Fact]
    public void Heikin_Ashi_still_reads_the_candle_anatomy()
    {
        var state = State(PriceChartStyle.Candles, 1, 100, 110) with { IsHeikinAshi = true };

        string spoken = Speak(state);

        Assert.Contains("Body", spoken);
        Assert.Contains("Upper wick", spoken);
    }
}
