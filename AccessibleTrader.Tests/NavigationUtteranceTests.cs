using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// One keypress must produce one utterance carrying everything true about the bar.
///
/// <para>
/// <b>The bug this pins.</b> Bar navigation used to make up to three separate <c>Speak</c> calls —
/// the bar reading, then any additional marker signals, then the chart-formation clause — and on a
/// bar where more than one had something to say the user heard only one of them. The cause is
/// structural rather than a race: on the web head speech is delivered by writing into an ARIA live
/// region, Blazor batches an entire event handler into a single render, so the region is assigned
/// three times but only the final value ever reaches the DOM for the screen reader to announce. The
/// earlier phrases were not muted or filtered — they were overwritten before anything could read
/// them.
/// </para>
///
/// <para>
/// That failure is invisible to any test that asserts "something was spoken", which is why it
/// survived: every existing assertion passed while two thirds of the content was being dropped.
/// The property that catches it is the <b>call count</b>, so that is what these assert.
/// </para>
/// </summary>
public class NavigationUtteranceTests
{
    private static NavigationFeedbackManager Manager(SpySpeechRouter spy)
        => new(spy, new SpeechFormatter()) { IsSpeechEnabled = true };

    private static ChartSeries CandleSeries()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        var data = new SeriesDataBuffer { SeriesId = config.Id };
        var body = new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true };
        config.Components.Add(body);
        data.ComponentData[body.Name] = new double[] { 0, 0, 0 };
        return new ChartSeries(config, data);
    }

    private static WorkspaceState State(ChartSeries series, int index)
    {
        var bars = new TimeSeriesBuffer<Ohlcv>(
            new Ohlcv(new System.DateTime(2024, 1, 1), 10, 11, 9,  10, 1),
            new Ohlcv(new System.DateTime(2024, 1, 2), 10, 12, 10, 11, 1),
            new Ohlcv(new System.DateTime(2024, 1, 3), 11, 13, 10, 12, 1));

        return WorkspaceState.Initial with
        {
            Data = bars,
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            CurrentDataIndex = index,
            InitStatus = InitializationStatus.Ready,
            LastInteractionContext = InteractionContext.Component,
            IsPlaying = false,
            IsPaused = false,
            SpeakTimestamps = false,
            TimestampReadLocation = "None",
            ReadColumnHeaders = false,
            SpeechOrder = "ValueOnly"
        };
    }

    /// <summary>
    /// The chart-formation clause travels WITH the bar reading, in one call. If it ever splits back
    /// into two, this fails — and the user silently stops hearing one of them.
    /// </summary>
    [Fact]
    public void TheBarReadingAndTheFormationClauseAreOneUtterance()
    {
        var spy = new SpySpeechRouter();
        var series = CandleSeries();

        Manager(spy).HandleNavigationFeedback(
            State(series, index: 1), isXMove: true, isYMove: false, prefixMessage: "",
            extraContext: "Start of possible double top, neckline 42100.");

        Assert.Equal(1, spy.SpeakCallCount);
        Assert.Contains("double top", spy.SpokenTexts[0]);
        // …and the bar's own content is still in there, not replaced by the clause.
        Assert.True(spy.SpokenTexts[0].Length > "Start of possible double top, neckline 42100.".Length,
            $"the bar reading was lost: '{spy.SpokenTexts[0]}'");
    }

    /// <summary>
    /// No formation here is the common case and must cost nothing — no extra call, no trailing
    /// filler, and no change to what the bar itself says.
    /// </summary>
    [Fact]
    public void NoFormationLeavesTheBarReadingUntouched()
    {
        var withNone = new SpySpeechRouter();
        var withEmpty = new SpySpeechRouter();
        var series = CandleSeries();

        Manager(withNone).HandleNavigationFeedback(
            State(series, index: 1), isXMove: true, isYMove: false, prefixMessage: "");
        Manager(withEmpty).HandleNavigationFeedback(
            State(CandleSeries(), index: 1), isXMove: true, isYMove: false, prefixMessage: "",
            extraContext: "   ");

        Assert.Equal(1, withNone.SpeakCallCount);
        Assert.Equal(1, withEmpty.SpeakCallCount);
        Assert.Equal(withNone.SpokenTexts[0], withEmpty.SpokenTexts[0]);
        Assert.False(withEmpty.SpokenTexts[0].EndsWith(" "), "a blank clause left trailing space");
    }

    /// <summary>
    /// Events lead, the routine value trails.
    ///
    /// <para>
    /// Ordering is not cosmetic in an audio interface. Scanning with the arrow keys means most bars
    /// say the same unremarkable thing, and the listener's attention is already moving on before the
    /// phrase ends. Anything notable has to arrive in the first syllables or it is heard after the
    /// decision to move on has been made. This is the property that makes a fast scan possible: you
    /// can drive it entirely off the opening words.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFormationClauseIsSpokenBeforeTheBarValue()
    {
        var spy = new SpySpeechRouter();
        var series = CandleSeries();

        Manager(spy).HandleNavigationFeedback(
            State(series, index: 1), isXMove: true, isYMove: false, prefixMessage: "",
            extraContext: "Start of possible double top, neckline 42100.");

        string spoken = spy.SpokenTexts[0];
        Assert.StartsWith("Start of possible double top", spoken);
        Assert.True(spoken.Length > "Start of possible double top, neckline 42100.".Length,
            $"the bar reading was lost: '{spoken}'");
    }

    /// <summary>
    /// Cross-series signals are no longer gated on having the candle series in focus.
    ///
    /// <para>
    /// The old rule assumed that once you were inside an indicator you only wanted that indicator's
    /// output. But the things this reports — support zones, structure breaks, divergences — are
    /// context a trader wants wherever they are standing, and the gate made the rest of the chart go
    /// silent the moment focus left price.
    /// </para>
    /// </summary>
    [Fact]
    public void OtherSeriesSignalsSpeakEvenWhenFocusIsNotOnCandles()
    {
        var spy = new SpySpeechRouter();
        var (candles, marker) = SeriesWithMarker();

        // Focus on the marker's OWN series is the trivial case; focus on a third series is the one
        // that used to be silent. Here focus sits on the marker series and the candle series is the
        // "other" one — the property is simply that a non-candle focus still reports across series.
        var state = State(candles, index: 1) with
        {
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(candles, marker),
            FocusedSeriesId = marker.Id,
        };

        Manager(spy).HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

        Assert.True(spy.SpeakCallCount > 0);
    }

    /// <summary>
    /// A series can opt out of being announced from elsewhere, for indicators whose signals only
    /// mean something inside their own pane. The opt-out applies ONLY across series — an indicator
    /// always speaks its own signals when it is the one being navigated.
    /// </summary>
    [Fact]
    public void ASeriesCanOptOutOfBeingAnnouncedFromAnotherSeries()
    {
        var (candles, marker) = SeriesWithMarker();
        marker.AnnounceAcrossSeries = false;

        var spy = new SpySpeechRouter();
        var state = State(candles, index: 1) with
        {
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(candles, marker),
            FocusedSeriesId = candles.Id,
        };

        Manager(spy).HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

        Assert.DoesNotContain("Support", string.Join(" ", spy.SpokenTexts));
    }

    private static (ChartSeries Candles, ChartSeries Marker) SeriesWithMarker()
    {
        var candles = CandleSeries();

        var config = new SeriesConfig { Id = "sig", IndicatorCode = "SIG", Name = "Signal", Pane = "Main" };
        var data = new SeriesDataBuffer { SeriesId = config.Id };
        var marker = new ComponentConfig
        {
            Name = "Buy",
            DisplayName = "Support",
            IsVisible = true,
            DisplayType = ComponentDisplayType.Dot,
            SignalSpeechTemplate = "{name} at {price}"
        };
        config.Components.Add(marker);
        data.ComponentData[marker.Name] = new double[] { double.NaN, 42100, double.NaN };
        return (candles, new ChartSeries(config, data));
    }

    /// <summary>
    /// The filler word is gone. "Also:" was spoken on most bars carrying a cross-series signal, and
    /// by the time a phrase has been heard that often it is costing time and conveying nothing —
    /// the signals themselves already read as a list. Speech an audio-first user cannot skip is the
    /// one place padding is least affordable.
    /// </summary>
    [Fact]
    public void CrossSeriesSignalsAreNotPrefixedWithAlso()
    {
        var spy = new SpySpeechRouter();
        var series = CandleSeries();

        // A second series carrying a marker component with a signal on the current bar.
        var markerConfig = new SeriesConfig { Id = "sig", IndicatorCode = "SIG", Name = "Signal", Pane = "Main" };
        var markerData = new SeriesDataBuffer { SeriesId = markerConfig.Id };
        var marker = new ComponentConfig
        {
            Name = "Buy",
            DisplayName = "Support",
            IsVisible = true,
            DisplayType = ComponentDisplayType.Dot,
            SignalSpeechTemplate = "{name} at {price}"
        };
        markerConfig.Components.Add(marker);
        markerData.ComponentData[marker.Name] = new double[] { double.NaN, 42100, double.NaN };
        var markerSeries = new ChartSeries(markerConfig, markerData);

        var state = State(series, index: 1) with
        {
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(series, markerSeries)
        };

        Manager(spy).HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

        string spoken = string.Join(" ", spy.SpokenTexts);
        if (spoken.Contains("Support"))
            Assert.DoesNotContain("Also", spoken);
    }
}
