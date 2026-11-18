using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests;

/// <summary>
/// Stage 4a's words: the surprise word in each direction, the bar clause's added phrase, the
/// detail's expected line, the release read-out, countdowns and the calendar.
/// </summary>
[Collection(nameof(SpeechClockSettingsTests))]
public class MarketEventExpectedWordingTests : IDisposable
{
    private static readonly DateTime Moment = new(2026, 3, 11, 8, 30, 0, DateTimeKind.Utc);

    public MarketEventExpectedWordingTests()
    {
        SpeechTimeFormatter.UseUtc = true;
        SpeechTimeFormatter.TwelveHour = false;
    }

    public void Dispose()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    private static MarketEventFigure Figure(
        string name, double? actual, double? expected,
        MarketEventFigureDirection direction = MarketEventFigureDirection.HigherIsHotter,
        MarketEventFigureUnit unit = MarketEventFigureUnit.Percent,
        double? previous = null)
        => new(name, actual, previous, unit, direction) { Expected = expected };

    private static MarketEvent Cpi(double? actual, double? expected, double? previous = 0.3)
        => MarketEventTestData.Event(
            id: "cpi-2026-03-11", spoken: "CPI", longName: "Consumer Price Index",
            moment: Moment,
            figures: new MarketEventFigures(new[]
            {
                Figure("CPI", actual, expected, previous: previous),
            }));

    private static MarketEvent Prices(
        MarketEventFigureDirection direction, double actual, double expected)
        => MarketEventTestData.Event(
            id: "jobs-2026-03-11", spoken: "jobs report", longName: "Nonfarm payrolls",
            moment: Moment,
            figures: new MarketEventFigures(new[]
            {
                Figure("payrolls", actual, expected, direction,
                    MarketEventFigureUnit.Thousands),
            }));

    [Theory]
    [InlineData(MarketEventFigureDirection.HigherIsHotter, 0.4, 0.3, "hotter than expected")]
    [InlineData(MarketEventFigureDirection.HigherIsHotter, 0.2, 0.3, "cooler than expected")]
    [InlineData(MarketEventFigureDirection.HigherIsStronger, 200, 128, "stronger than expected")]
    [InlineData(MarketEventFigureDirection.HigherIsStronger, 57, 128, "weaker than expected")]
    [InlineData(MarketEventFigureDirection.HigherIsWeaker, 4.4, 4.3, "weaker than expected")]
    [InlineData(MarketEventFigureDirection.HigherIsWeaker, 4.2, 4.3, "stronger than expected")]
    public void TheSurpriseWordFollowsTheFiguresDirection(
        MarketEventFigureDirection direction, double actual, double expected, string word)
    {
        var unit = direction == MarketEventFigureDirection.HigherIsWeaker
            ? MarketEventFigureUnit.Percent
            : actual > 10 ? MarketEventFigureUnit.Thousands : MarketEventFigureUnit.Percent;
        var figure = Figure("test", actual, expected, direction, unit);

        Assert.Equal(word, MarketEventSpeech.SurpriseWord(figure));
    }

    [Fact]
    public void RoundingBothToTheStepCanMakeItAsExpected()
    {
        var figure = Figure("CPI", 0.34, 0.3);

        Assert.Equal("as expected", MarketEventSpeech.SurpriseWord(figure));
    }

    [Fact]
    public void NoExpectedMeansNoSurpriseWord()
    {
        Assert.Equal("", MarketEventSpeech.SurpriseWord(Figure("CPI", 0.4, null)));
        Assert.Equal("", MarketEventSpeech.SurpriseWord(Figure("CPI", null, 0.3)));
    }

    [Fact]
    public void TheBarClauseAddsTheWordAfterTheFigureItAlreadySays()
    {
        string said = MarketEventSpeech.Clause(
            new[] { MarketEventTestData.Placed(Cpi(0.4, 0.3), 0) },
            1800, MarketEventLevel.All, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Equal(
            $"CPI {MarketEventSpeech.TimeText(Cpi(0.4, 0.3))}, up 0.4% on the month, "
            + "previous up 0.3%, hotter than expected",
            said);
    }

    [Fact]
    public void ACalendarLinePutsTheExpectedFigureAfterImportance()
    {
        Assert.Equal(
            "08:30, CPI, important, expected 0.3, up 0.4% on the month, previous up 0.3%.",
            MarketEventCalendarWording.EventLine(Cpi(0.4, 0.3), Moment.AddDays(1)));
        Assert.Equal(
            "08:30, CPI, important, expected -0.1, previous up 0.3%.",
            MarketEventCalendarWording.EventLine(Cpi(null, -0.1), Moment.AddHours(-1)));
    }

    [Fact]
    public void TheHistoryLineGetsTheExpectedFigureAndTheWord()
    {
        Assert.Equal(
            "11 March 2026, up 0.4% on the month, previous up 0.3%, "
            + "expected 0.3, by Kalshi, hotter than expected.",
            MarketEventCalendarWording.HistoryLine(Cpi(0.4, 0.3), null));
    }

    [Fact]
    public void TheCountdownNamesAProvisionalExpectedFigure()
    {
        Assert.Equal(
            "CPI, expected -0.1, in 15 minutes.",
            MarketEventNoticeWording.Countdown(
                Cpi(null, -0.1), 15, Array.Empty<MarketEventHeldPosition>()));
        Assert.Equal(
            "CPI in 15 minutes.",
            MarketEventNoticeWording.Countdown(
                Cpi(null, null), 15, Array.Empty<MarketEventHeldPosition>()));
    }

    [Fact]
    public void TheReadOutUsesTheReleaseFigureWithExpectedAndTheShortWord()
    {
        Assert.Equal(
            "CPI out. CPI 0.4, expected 0.3, hotter.",
            MarketEventNoticeWording.ReadOut(
                Cpi(0.4, 0.3), Array.Empty<MarketEventPriceSample>(), awaitingFigure: false));
    }

    [Fact]
    public void PayrollsKeepTheThousandsWordsInTheReleaseReadOut()
    {
        Assert.Equal(
            "jobs report out. payrolls up 57 thousand, expected up 128 thousand, weaker.",
            MarketEventNoticeWording.ReadOut(
                Prices(MarketEventFigureDirection.HigherIsStronger, 57, 128),
                Array.Empty<MarketEventPriceSample>(), awaitingFigure: false));
    }

    [Fact]
    public void TheDetailGainsTheSourceAndTheWordOnEveryFigureLine()
    {
        var e = MarketEventTestData.Event(
            longName: "Consumer Price Index", moment: Moment,
            figures: new MarketEventFigures(new[]
            {
                Figure("CPI", 0.4, 0.3),
                Figure("core CPI", 0.2, 0.3),
            }));
        var bar = MarketEventTestData.Bar(Moment);

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar,
            MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains("Figure: CPI up 0.4% on the month, expected 0.3, by Kalshi, hotter than expected.", said);
        Assert.Contains("Core CPI: up 0.2% on the month, expected 0.3, by Kalshi, cooler than expected", said);
    }

    [Fact]
    public void TheDetailOfAnOldBarSaysNothingAboutExpected()
    {
        string said = MarketEventSpeech.FigureDetail(Cpi(0.4, null));

        Assert.DoesNotContain("expected", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kalshi", said);
    }
}
