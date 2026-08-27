using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using System.Collections.Immutable;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>With Heikin-Ashi on, spoken candle anatomy describes the HA candle — the one that is drawn.</b>
///
/// <para>
/// This was reported from live use and the numbers were never wrong; they were about the wrong
/// bar. Heikin-Ashi candles routinely have NO shadow on one side — that shaved look is the whole
/// visual signature of a trending HA series — while the raw bar underneath still has one. So the
/// terminal announced a lower wick of nineteen percent for a candle drawn without a lower wick at
/// all, and there was no way to tell from the audio which candle was being described.
/// </para>
///
/// <para>
/// <c>BarDetailService.BarAsDrawn</c> was fixed for the bar-detail key. The other two readouts —
/// the series-context summary and the component-context wick value — go through
/// <c>NavigationFeedbackManager</c>, which applies its own transform, and had no test. The gap
/// matters because they are the ones a user hears on every arrow keypress; the detail key is the
/// one they press deliberately.
/// </para>
///
/// <para>
/// The fixture below is arithmetic, not a guess. Every expected number is derived in the comment
/// beside it from the published HA formula, and the bar is shaped so that raw and HA disagree in
/// the specific way that produced the report: a real lower shadow on the raw bar, none at all on
/// the HA one.
/// </para>
/// </summary>
public sealed class HeikinAshiSpeechTests
{
    // ── The fixture ─────────────────────────────────────────────────────────────────
    //
    // Bar 0 is flat at 90, which pins the HA seed:
    //     haClose0 = (90+90+90+90)/4 = 90
    //     haOpen0  = (open0 + close0)/2 = 90          [the series' first-bar rule]
    //
    // Bar 1 is the one under test — raw O=100 H=110 L=97.5 C=108:
    //     raw range = 110 − 97.5 = 12.5
    //     raw lower shadow = min(O,C) − L = 100 − 97.5 = 2.5   →  2.5/12.5 = 20%
    //
    //     haClose1 = (100+110+97.5+108)/4 = 103.875
    //     haOpen1  = (haOpen0 + haClose0)/2 = (90+90)/2 = 90
    //     haHigh1  = max(110, 90, 103.875) = 110
    //     haLow1   = min(97.5, 90, 103.875) = 90
    //     HA range = 110 − 90 = 20
    //     HA lower shadow = min(haOpen,haClose) − haLow = 90 − 90 = 0   →  0%
    //     HA body  = |103.875 − 90| = 13.875                            →  69%
    //     HA upper = 110 − max(90, 103.875) = 6.125                     →  31%
    //
    // So the raw bar says "Lower wick 20%" and the HA bar says "Lower wick 0%", which is exactly
    // the shape of the original report.

    private const double HaLow = 90.0;
    private const double RawLow = 97.5;

    private static ChartSeries CandleSeries()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        var data = new SeriesDataBuffer { SeriesId = config.Id };
        config.Components.Add(new ComponentConfig
        {
            Name = "lower_wick", DisplayName = "Lower Wick", IsVisible = true,
            DisplayType = ComponentDisplayType.Wick, Role = ComponentRole.Wick,
        });
        return new ChartSeries(config, data);
    }

    private static WorkspaceState State(bool heikinAshi, InteractionContext context)
    {
        var bars = new TimeSeriesBuffer<Ohlcv>(
            new Ohlcv(new DateTime(2026, 1, 1), 90, 90, 90, 90, 1),
            new Ohlcv(new DateTime(2026, 1, 2), 100, 110, RawLow, 108, 1));

        var series = CandleSeries();
        return WorkspaceState.Initial with
        {
            Data = bars,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 1,
            IsHeikinAshi = heikinAshi,
            InitStatus = InitializationStatus.Ready,
            LastInteractionContext = context,
            SpeakTimestamps = false,
            TimestampReadLocation = "None",
            ReadColumnHeaders = false,
            SpeechOrder = "ValueOnly",
        };
    }

    private static string Speak(bool heikinAshi, InteractionContext context)
    {
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(State(heikinAshi, context),
                isXMove: true, isYMove: false, prefixMessage: "");

        Assert.NotEmpty(spy.SpokenTexts);
        return string.Join(" ", spy.SpokenTexts);
    }

    // ── Series context: the anatomy summary ─────────────────────────────────────────

    /// <summary>
    /// The headline case. With HA on, the candle drawn on screen has no lower shadow, so the
    /// spoken anatomy has to say so.
    /// </summary>
    [Fact]
    public void WithHeikinAshiOn_TheLowerWickIsSpokenAsZero()
    {
        string spoken = Speak(heikinAshi: true, InteractionContext.Series);

        Assert.Contains("Lower wick 0%", spoken);
    }

    /// <summary>
    /// The vacuity twin, and the one that makes the test above mean something. With HA OFF the
    /// same bar has a real lower shadow and must report it — otherwise "Lower wick 0%" could be
    /// coming from a formatter that says zero for everything.
    /// </summary>
    [Fact]
    public void WithHeikinAshiOff_TheSameBarReportsItsRealLowerWick()
    {
        string spoken = Speak(heikinAshi: false, InteractionContext.Series);

        Assert.Contains("Lower wick 20%", spoken);
        Assert.DoesNotContain("Lower wick 0%", spoken);
    }

    /// <summary>
    /// The other two percentages move with it. A transform applied to one field and not the rest
    /// would produce an anatomy that does not add up — the failure mode of a partial fix.
    /// </summary>
    [Fact]
    public void TheWholeAnatomyDescribesTheSameCandle()
    {
        string spoken = Speak(heikinAshi: true, InteractionContext.Series);

        Assert.Contains("Body 69%", spoken);
        Assert.Contains("Upper wick 31%", spoken);
        Assert.Contains("Lower wick 0%", spoken);
    }

    /// <summary>
    /// The OHLC values are the HA candle's too, not the raw bar's. Reading a raw low of 97.50
    /// beside an HA anatomy would be two different candles in one sentence.
    /// </summary>
    [Fact]
    public void TheSpokenLowIsTheHeikinAshiLow()
    {
        string spoken = Speak(heikinAshi: true, InteractionContext.Series);

        Assert.Contains("Low 90", spoken);
        Assert.DoesNotContain("97.50", spoken);
    }

    // ── Component context: the wick's own value ─────────────────────────────────────

    /// <summary>
    /// Standing ON the lower wick component and reading its value is the third readout, and it
    /// resolves through a different path again — <c>SpeechFormatter.GetPointValue</c>'s fallback
    /// for the price series, whose components are virtual and carry no data array. It must land on
    /// the HA low as well.
    /// </summary>
    [Fact]
    public void TheLowerWickComponentReadsTheHeikinAshiLow()
    {
        string spoken = Speak(heikinAshi: true, InteractionContext.Component);

        Assert.Contains(HaLow.ToString("0.##"), spoken);
        Assert.DoesNotContain(RawLow.ToString("0.##"), spoken);
    }

    /// <summary>Same component with HA off reads the raw low — the vacuity half of the above.</summary>
    [Fact]
    public void TheLowerWickComponentReadsTheRawLowWhenHeikinAshiIsOff()
    {
        string spoken = Speak(heikinAshi: false, InteractionContext.Component);

        Assert.Contains(RawLow.ToString("0.##"), spoken);
    }
}
