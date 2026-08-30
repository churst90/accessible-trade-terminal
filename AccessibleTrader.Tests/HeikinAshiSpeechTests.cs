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

    /// <summary>
    /// The candle series as PRODUCTION builds it: three components carrying a
    /// <c>DataMapping</c>, and the raw OHLCV arrays <c>ViewportReducer.SyncMappedComponentData</c>
    /// syncs into them on every data update.
    ///
    /// <para>
    /// The arrays are the point. This fixture used to hand back an empty
    /// <see cref="SeriesDataBuffer"/> with a single component, and the component-context test
    /// below passed because of it: <c>SpeechFormatter.GetPointValue</c> tries the component's
    /// array first and only falls through to the bar when the lookup misses. With no arrays it
    /// always missed, so the test proved the fallback worked and said nothing at all about the
    /// path a user actually walks. On a real chart the arrays are there, they are raw, and the
    /// wick spoke the raw low while the summary described the Heikin-Ashi candle — the very
    /// defect this file was written to fence off, still live one keypress away.
    /// </para>
    /// </summary>
    private static ChartSeries CandleSeries(IReadOnlyList<Ohlcv> bars)
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

        var data = new SeriesDataBuffer { SeriesId = config.Id, FirstBarDate = bars[0].Date };
        data.ComponentData["upper_wick"] = bars.Select(b => (double)b.High).ToArray();
        data.ComponentData["body"]       = bars.Select(b => (double)b.Close).ToArray();
        data.ComponentData["lower_wick"] = bars.Select(b => (double)b.Low).ToArray();
        return new ChartSeries(config, data);
    }

    /// <summary>Index of <c>lower_wick</c> in the production component order.</summary>
    private const int LowerWickComponent = 2;

    private static WorkspaceState State(bool heikinAshi, InteractionContext context)
    {
        var bars = new TimeSeriesBuffer<Ohlcv>(
            new Ohlcv(new DateTime(2026, 1, 1), 90, 90, 90, 90, 1),
            new Ohlcv(new DateTime(2026, 1, 2), 100, 110, RawLow, 108, 1));

        var series = CandleSeries(bars);
        return WorkspaceState.Initial with
        {
            Data = bars,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = LowerWickComponent,
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

    // ── The close line is NOT a candle ──────────────────────────────────────────────
    //
    // Reported from live use: one Bitstamp BTC daily chart quoting three different closes at
    // once — the browser title, the candle readout, and the close line. The candle is expected
    // to differ, because a Heikin-Ashi candle IS a different candle. The other two are the same
    // number by construction: the title reads state.Data[^1].Close and the line is RENDERED
    // from the raw close, so a line that speaks the Heikin-Ashi average is disagreeing with the
    // pixels drawn for it as well as with the title.
    //
    // It disagreed with ITSELF too, which is what made it undiagnosable by ear: the series
    // summary took the transformed bar, and arrowing one keypress into the same series took the
    // mapped array, which is raw. Two numbers, one series, nothing announcing the switch.

    private const double RawClose = 108.0;   // bar 1's real close
    private const double HaClose  = 103.875; // (100+110+97.5+108)/4 — see the fixture arithmetic

    private static ChartSeries PriceLineSeries(IReadOnlyList<Ohlcv> bars)
    {
        var config = new SeriesConfig { Id = "price", IndicatorCode = "PRICE", Name = "Price" };
        config.Components.Add(new ComponentConfig
        {
            Name = "line", DisplayName = "Price", IsVisible = true,
            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.PriceAction,
            DataMapping = "close", SpeechTemplate = "{name}. {type}. {value:price}.",
        });

        var data = new SeriesDataBuffer { SeriesId = config.Id, FirstBarDate = bars[0].Date };
        data.ComponentData["line"] = bars.Select(b => (double)b.Close).ToArray();
        return new ChartSeries(config, data);
    }

    private static string SpeakPriceLine(bool heikinAshi, InteractionContext context)
    {
        var baseState = State(heikinAshi, context);
        var price = PriceLineSeries(baseState.Data);
        var state = baseState with
        {
            ActiveSeries = baseState.ActiveSeries.Add(price),
            FocusedSeriesId = price.Id,
            FocusedComponentIndex = 0,
        };

        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter()) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

        Assert.NotEmpty(spy.SpokenTexts);
        return string.Join(" ", spy.SpokenTexts);
    }

    /// <summary>
    /// The headline of the second half. With HA on the close line still reads the raw close.
    /// </summary>
    [Theory]
    [InlineData(InteractionContext.Series)]
    [InlineData(InteractionContext.Component)]
    public void ThePriceLineReadsTheRawCloseWithHeikinAshiOn(InteractionContext context)
    {
        string spoken = SpeakPriceLine(heikinAshi: true, context);

        Assert.Contains(SpeechPriceFormatter.FormatPrice(RawClose), spoken);
        Assert.DoesNotContain(SpeechPriceFormatter.FormatPrice(HaClose), spoken);
    }

    /// <summary>
    /// The vacuity twin: the two numbers have to be far enough apart that "reads the raw close"
    /// is a claim and not an accident of formatting. 108.00 against 103.88 is 4.2 points on a
    /// bar with a 12.5-point range.
    /// </summary>
    [Fact]
    public void TheTwoCandidateClosesAreActuallyDifferentNumbers()
    {
        Assert.NotEqual(SpeechPriceFormatter.FormatPrice(RawClose), SpeechPriceFormatter.FormatPrice(HaClose));
    }

    /// <summary>
    /// The line and the title bar are one number. The title (MainLayout.GetBrowserTitle) formats
    /// <c>state.Data[^1].Close</c> through this same formatter, so the assertion is written the
    /// way the title computes it rather than against a literal — if the title's source ever
    /// changes, this is the test that should have to change with it.
    ///
    /// <para>
    /// Both contexts, because the disagreement was BETWEEN them: whichever number this test
    /// pinned, checking only one context would have left the other free to say the other thing.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(InteractionContext.Series)]
    [InlineData(InteractionContext.Component)]
    public void ThePriceLineAgreesWithTheBrowserTitle(InteractionContext context)
    {
        var state = State(heikinAshi: true, context);
        string titleBarPrice = SpeechPriceFormatter.FormatPrice(state.Data[^1].Close);

        Assert.Contains(titleBarPrice, SpeakPriceLine(heikinAshi: true, context));
    }

    /// <summary>
    /// And the candle, on the same bar and the same keypress, is the one that differs — the
    /// control that stops the four tests above from being satisfied by a build that simply
    /// switched Heikin-Ashi off.
    /// </summary>
    [Fact]
    public void TheCandleStillReadsTheHeikinAshiCloseOnTheSameBar()
    {
        string spoken = Speak(heikinAshi: true, InteractionContext.Series);

        Assert.Contains(SpeechPriceFormatter.FormatPrice(HaClose), spoken);
        Assert.DoesNotContain(SpeechPriceFormatter.FormatPrice(RawClose), spoken);
    }
}
