using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests;

/// <summary>
/// The shipped seed: 3,509 dates from five sources, each with its URL and fetch date, and times
/// that survive daylight saving.
/// </summary>
public class MarketEventSeedTests
{
    private static MarketEventSeedDocument Seed() => MarketEventSeed.Load();

    [Fact]
    public void TheSeedHasTheInputFilesCounts()
    {
        var events = Seed().Events;

        Assert.Equal(933, events.Count(e => e.Id.StartsWith("cpi-", StringComparison.Ordinal)));
        Assert.Equal(859, events.Count(e => e.Id.StartsWith("nfp-", StringComparison.Ordinal)));
        Assert.Equal(587, events.Count(e => e.Id.StartsWith("ppi-", StringComparison.Ordinal)));
        Assert.Equal(812, events.Count(e => e.Id.StartsWith("gdp-", StringComparison.Ordinal)));
        Assert.Equal(248, events.Count(e => e.Id.StartsWith("fomc-", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheHeaderNamesEachSourceWithItsUrlAndFetchDate()
    {
        var sources = Seed().Sources;

        Assert.Equal(5, sources.Count);
        Assert.Contains(sources, s => s.Id == "cpi" && s.Url.Contains("release_id=10") && s.Fetched == "2026-11-21");
        Assert.Contains(sources, s => s.Id == "nfp" && s.Url.Contains("release_id=50") && s.Fetched == "2026-11-21");
        Assert.Contains(sources, s => s.Id == "ppi" && s.Url.Contains("release_id=46") && s.Fetched == "2026-11-21");
        Assert.Contains(sources, s => s.Id == "gdp" && s.Url.Contains("release_id=53") && s.Fetched == "2026-11-21");
        Assert.Contains(sources, s => s.Id == "fomc" && s.Fetched == "2026-07-31");
        Assert.All(sources, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.Url));
            Assert.False(string.IsNullOrWhiteSpace(s.Fetched));
        });
    }

    [Fact]
    public void CorrectionAndRevisionDaysAreNotInTheSeed()
    {
        var events = Seed().Events;

        Assert.DoesNotContain(events, e => e.Id == "cpi-2024-02-09");
        Assert.DoesNotContain(events, e => e.Id == "nfp-2020-05-11");
        Assert.DoesNotContain(events, e => e.Id == "nfp-2024-01-10");
        Assert.DoesNotContain(events, e => e.Id == "nfp-2012-12-12");
    }

    [Fact]
    public void TheRealReleasesBesideThoseRevisionDaysStayWithFigures()
    {
        var events = Seed().Events;

        foreach (string id in new[] { "cpi-2024-02-13", "nfp-2020-05-08", "nfp-2024-01-05", "nfp-2012-12-07" })
        {
            var e = Assert.Single(events, x => x.Id == id);
            Assert.NotNull(e.Figures);
        }
    }

    [Fact]
    public void The1960sDoubleMonthsAreKept()
    {
        var events = Seed().Events;

        Assert.Contains(events, e => e.Id == "nfp-1961-05-02");
        Assert.Contains(events, e => e.Id == "nfp-1961-05-31");
    }

    [Fact]
    public void PpiFebruaryRevisionDaysAreDroppedAndTheOtherDoubleMonthsAreKept()
    {
        var events = Seed().Events;

        Assert.DoesNotContain(events, e => e.Id == "ppi-2024-02-14");
        Assert.Contains(events, e => e.Id == "ppi-2024-02-16");
        Assert.Contains(events, e => e.Id == "ppi-2026-01-14");
        Assert.Contains(events, e => e.Id == "ppi-2026-01-30");
    }

    [Fact]
    public void TheFirstAndLastDatesMatchTheInputs()
    {
        var events = Seed().Events;

        Assert.Equal(new DateOnly(1949, 3, 24), events.Where(e => e.Id.StartsWith("cpi-", StringComparison.Ordinal)).Min(e => e.Date));
        Assert.Equal(new DateOnly(2026, 12, 10), events.Where(e => e.Id.StartsWith("cpi-", StringComparison.Ordinal)).Max(e => e.Date));
        Assert.Equal(new DateOnly(1955, 5, 6), events.Where(e => e.Id.StartsWith("nfp-", StringComparison.Ordinal)).Min(e => e.Date));
        Assert.Equal(new DateOnly(2026, 12, 4), events.Where(e => e.Id.StartsWith("nfp-", StringComparison.Ordinal)).Max(e => e.Date));
        Assert.Equal(new DateOnly(1978, 2, 10), events.Where(e => e.Id.StartsWith("ppi-", StringComparison.Ordinal)).Min(e => e.Date));
        Assert.Equal(new DateOnly(2026, 12, 15), events.Where(e => e.Id.StartsWith("ppi-", StringComparison.Ordinal)).Max(e => e.Date));
        Assert.Equal(new DateOnly(1947, 7, 20), events.Where(e => e.Id.StartsWith("gdp-", StringComparison.Ordinal)).Min(e => e.Date));
        Assert.Equal(new DateOnly(2026, 12, 23), events.Where(e => e.Id.StartsWith("gdp-", StringComparison.Ordinal)).Max(e => e.Date));
        Assert.Equal(new DateOnly(2000, 2, 2), events.Where(e => e.Id.StartsWith("fomc-", StringComparison.Ordinal)).Min(e => e.Date));
        Assert.Equal(new DateOnly(2027, 12, 8), events.Where(e => e.Id.StartsWith("fomc-", StringComparison.Ordinal)).Max(e => e.Date));
    }

    [Fact]
    public void DaylightSavingPutsCpiAt1230UtcInMarchAnd1330UtcInNovember()
    {
        var events = Seed().Events;

        Assert.Equal(new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc),
            events.Single(e => e.Id == "cpi-2024-03-12").MomentUtc);
        Assert.Equal(new DateTime(2024, 11, 13, 13, 30, 0, DateTimeKind.Utc),
            events.Single(e => e.Id == "cpi-2024-11-13").MomentUtc);
    }

    [Fact]
    public void TheSeptemberFedDecisionIs1800Utc()
    {
        var fed = Seed().Events.Single(e => e.Id == "fomc-2024-09-18");

        Assert.Equal(new DateTime(2024, 9, 18, 18, 0, 0, DateTimeKind.Utc), fed.MomentUtc);
        Assert.Equal(MarketEventPrecision.Exact, fed.Precision);
    }

    [Fact]
    public void ReleasesBefore1997AndFedDecisionsBefore2013AreDayOnly()
    {
        var events = Seed().Events;

        Assert.Equal(MarketEventPrecision.DayOnly, events.Single(e => e.Id == "cpi-1996-12-12").Precision);
        Assert.Equal(MarketEventPrecision.Exact, events.Single(e => e.Id == "cpi-1997-01-14").Precision);
        Assert.Equal(MarketEventPrecision.DayOnly, events.Single(e => e.Id == "fomc-2012-12-12").Precision);
        Assert.Equal(MarketEventPrecision.Exact, events.Single(e => e.Id == "fomc-2013-01-30").Precision);
    }

    [Fact]
    public void UnscheduledFedDecisionsAreAlwaysDayOnly()
    {
        var unscheduled = Seed().Events
            .Where(e => e.Kind == MarketEventKind.Fed && e.SpokenName == "Fed decision, unscheduled")
            .ToList();

        Assert.NotEmpty(unscheduled);
        Assert.All(unscheduled, e => Assert.Equal(MarketEventPrecision.DayOnly, e.Precision));
    }

    [Fact]
    public void EverySeedEventTouchesEveryMarket()
    {
        var events = Seed().Events;

        Assert.All(events, e =>
            Assert.Equal(Enum.GetValues<MarketType>().Length, e.Scope.Count));
    }
}
