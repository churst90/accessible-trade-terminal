using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Alt+E calendar's words: the day heading, one event line (past with a figure, future with
/// only a previous, day-only "all day"), the button names, the alert announcements and the
/// release history. Pure functions, a fake clock where needed, no bus and no speech.
/// </summary>
[Collection(nameof(SpeechClockSettingsTests))]
public class MarketEventCalendarWordingTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PastMoment = new(2026, 3, 11, 8, 30, 0, DateTimeKind.Utc);

    public MarketEventCalendarWordingTests()
    {
        SpeechTimeFormatter.UseUtc = true;
        SpeechTimeFormatter.TwelveHour = false;
    }

    public void Dispose()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    private static MarketEventFigures CpiFigures(double? actual, double? previous) => new(new[]
    {
        new MarketEventFigure("CPI", actual, previous,
            MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
    });

    private static MarketEvent Cpi(
        DateTime? moment = null,
        MarketEventFigures? figures = null,
        MarketEventImportance importance = MarketEventImportance.High)
        => MarketEventTestData.Event(
            id: "cpi-2026-03-11", spoken: "CPI", importance: importance,
            moment: moment ?? PastMoment, figures: figures);

    [Fact]
    public void TheDayHeadingIsTheWeekdayThenTheDate()
    {
        Assert.Equal("Wednesday 11 March 2026",
            MarketEventCalendarWording.DayHeading(new DateOnly(2026, 3, 11)));
    }

    [Fact]
    public void APastEventReadsItsActualInTheExistingFigureWords()
    {
        var e = Cpi(figures: CpiFigures(3.1, 2.9));

        Assert.Equal(
            "08:30, CPI, important, up 3.1% on the month, previous up 2.9%.",
            MarketEventCalendarWording.EventLine(e, Now.AddDays(2)));
    }

    [Fact]
    public void AFutureEventReadsOnlyItsPreviousFigure()
    {
        var e = Cpi(figures: CpiFigures(null, 2.9));

        Assert.Equal(
            "08:30, CPI, important, previous up 2.9%.",
            MarketEventCalendarWording.EventLine(e, Now));
    }

    [Fact]
    public void AFutureEventWithNoFigureSaysNothingAboutFigures()
    {
        var fomc = MarketEventTestData.Event(
            id: "fomc-2026-03-18", kind: MarketEventKind.Fed, spoken: "Fed decision",
            longName: "FOMC decision", moment: new DateTime(2026, 3, 18, 18, 0, 0, DateTimeKind.Utc));

        Assert.Equal("18:00, Fed decision, important.",
            MarketEventCalendarWording.EventLine(fomc, Now));
    }

    [Fact]
    public void ADayOnlyEventSaysAllDay()
    {
        var expiry = MarketEventTestData.Event(
            id: "stock-expiry-2026-03-20", kind: MarketEventKind.Expiry,
            spoken: "options expiry", importance: MarketEventImportance.Medium,
            moment: new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2026, 3, 20),
            scope: new[] { MarketType.Stock });

        Assert.Equal("all day, options expiry, medium.",
            MarketEventCalendarWording.EventLine(expiry, Now));
    }

    [Fact]
    public void TheTimeFollowsTheF12Clock()
    {
        var e = Cpi();

        Assert.StartsWith("08:30, CPI", MarketEventCalendarWording.EventLine(e, Now));

        SpeechTimeFormatter.TwelveHour = true;
        Assert.StartsWith("8:30 AM, CPI", MarketEventCalendarWording.EventLine(e, Now));

        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
        // Whichever local zone the test machine runs in, the event's own date is still the
        // heading's date; this assertion is only that the helper, not the calendar, chose it.
        Assert.Equal("11 March 2026", MarketEventCalendarWording.LongDate(e.Date));
    }

    [Fact]
    public void TheImportanceWordsAreTheCalendarsShortOnes()
    {
        Assert.Equal("important", MarketEventCalendarWording.ImportanceWord(MarketEventImportance.High));
        Assert.Equal("medium", MarketEventCalendarWording.ImportanceWord(MarketEventImportance.Medium));
        Assert.Equal("minor", MarketEventCalendarWording.ImportanceWord(MarketEventImportance.Low));
    }

    [Fact]
    public void TheButtonNamesIncludeTheEvent()
    {
        var e = Cpi();
        Assert.Equal("Alert me before CPI, 11 March", MarketEventCalendarWording.WatchLabel(e));
        Assert.Equal("History for CPI, 11 March", MarketEventCalendarWording.HistoryLabel(e));
    }

    [Fact]
    public void TheNotOnTheBarsSentenceNamesTheEventAndItsDay()
    {
        Assert.Equal("CPI on 11 March 2026 is not on this chart's bars.",
            MarketEventCalendarWording.NotOnBars(Cpi()));
    }

    [Fact]
    public void TheAlertAnnouncementsSayWhatChangedAndTheLeads()
    {
        var e = Cpi();

        Assert.Equal("Alert set for CPI, 11 March, 15 and 1 minutes before.",
            MarketEventCalendarWording.WatchAnnouncement(e, true, new[] { 15, 1 }));
        Assert.Equal("Alert removed for CPI, 11 March.",
            MarketEventCalendarWording.WatchAnnouncement(e, false, new[] { 15, 1 }));
    }

    [Fact]
    public void TheOnlyTouchingTestKeepsAHeldMarketAndDropsAnEventForNeither()
    {
        var stock = MarketEventTestData.Event(
            id: "stock-event", scope: new[] { MarketType.Stock });

        Assert.False(MarketEventNoticePlanner.Touches(
            stock, Array.Empty<MarketType>(), Array.Empty<MarketType>()));
        Assert.True(MarketEventNoticePlanner.Touches(
            stock, Array.Empty<MarketType>(), new[] { MarketType.Stock }));
        Assert.True(MarketEventNoticePlanner.Touches(
            stock, new[] { MarketType.Stock }, Array.Empty<MarketType>()));
    }

    [Fact]
    public void HistoryIsTheLastSixOfTheSameSourceNewestFirst()
    {
        var all = MarketEventSeed.Load().Events;
        var e = all.Single(x => x.Id == "cpi-2024-03-12");

        var history = MarketEventCalendarWording.History(e, all);

        Assert.Equal(new[]
        {
            "cpi-2024-02-13",
            "cpi-2024-01-11",
            "cpi-2023-12-12",
            "cpi-2023-11-14",
            "cpi-2023-10-12",
            "cpi-2023-09-13",
        }, history.Select(x => x.Id));
    }

    [Fact]
    public void TheFirstSeedDateHasNoEarlierReleases()
    {
        var all = MarketEventSeed.Load().Events;
        var e = all.Single(x => x.Id == "cpi-1949-03-24");

        Assert.Empty(MarketEventCalendarWording.History(e, all));
        Assert.Equal("No earlier releases.", MarketEventCalendarWording.NoEarlierReleases);
    }

    [Fact]
    public void AHistoryLineHasTheFigureAndThePriceWhenTheBarIsLoaded()
    {
        var all = MarketEventSeed.Load().Events;
        var e = all.Single(x => x.Id == "cpi-2024-02-13");
        var bar = MarketEventTestData.Bar(
            new DateTime(2024, 2, 13, 0, 0, 0, DateTimeKind.Utc), open: 100, close: 98.8);

        string withBar = MarketEventCalendarWording.HistoryLine(e, bar);
        Assert.StartsWith("13 February 2024, up 0.3% on the month, previous up 0.2%;", withBar);
        Assert.Contains("Price fell 1.2% over this bar.", withBar);

        string withoutBar = MarketEventCalendarWording.HistoryLine(e, null);
        Assert.StartsWith("13 February 2024, up 0.3% on the month, previous up 0.2%.", withoutBar);
        Assert.DoesNotContain("Price", withoutBar);
    }

    [Fact]
    public void AReleaseWithNoFigureSaysNoFigure()
    {
        var ppi = MarketEventSeed.Load().Events.Single(x => x.Id == "ppi-2024-02-16");

        Assert.Equal("16 February 2024, no figure.",
            MarketEventCalendarWording.HistoryLine(ppi, null));
    }

    [Fact]
    public void ADayOnlyReleaseHasHistoryWithNoFigurePart()
    {
        var expiry = MarketEventTestData.Event(
            id: "stock-expiry-2026-03-20", kind: MarketEventKind.Expiry,
            spoken: "options expiry", precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2026, 3, 20),
            moment: new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc),
            scope: new[] { MarketType.Stock });

        Assert.Equal("20 March 2026, no figure.",
            MarketEventCalendarWording.HistoryLine(expiry, null));
    }
}
