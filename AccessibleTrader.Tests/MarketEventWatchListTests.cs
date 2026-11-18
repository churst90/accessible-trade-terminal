using AccessibleTrader.Core.Services.MarketEvents;

namespace AccessibleTrader.Tests;

/// <summary>
/// The watched ids are pruned whenever they are written: an old event's id goes, tomorrow's
/// stays, and an id the caller cannot resolve is not silently dropped.
/// </summary>
public class MarketEventWatchListTests
{
    private static readonly DateTime Now = new(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnIdThreeDaysOldGoesAndAnIdForTomorrowStays()
    {
        var old = MarketEventTestData.Event(
            id: "cpi-2026-03-08", moment: Now.AddDays(-3),
            date: DateOnly.FromDateTime(Now.AddDays(-3)));
        var tomorrow = MarketEventTestData.Event(
            id: "cpi-2026-03-12", moment: Now.AddDays(1),
            date: DateOnly.FromDateTime(Now.AddDays(1)));

        var kept = MarketEventWatchList.Prune(
            new[] { old.Id, tomorrow.Id }, new[] { old, tomorrow }, Now);

        Assert.Equal(new[] { tomorrow.Id }, kept);
    }

    [Fact]
    public void AnIdTwoDaysOldStillStays()
    {
        var justOld = MarketEventTestData.Event(
            id: "cpi-2026-03-09", moment: Now.AddDays(-2),
            date: DateOnly.FromDateTime(Now.AddDays(-2)));

        var kept = MarketEventWatchList.Prune(new[] { justOld.Id }, new[] { justOld }, Now);

        Assert.Equal(new[] { justOld.Id }, kept);
    }

    [Fact]
    public void AnUnresolvedIdWithAnOldDateSuffixIsPrunedAndAnUndatedOneIsKept()
    {
        var kept = MarketEventWatchList.Prune(
            new[] { "cpi-2026-03-01", "something-without-a-date" },
            Array.Empty<MarketEvent>(), Now);

        Assert.Equal(new[] { "something-without-a-date" }, kept);
    }

    [Fact]
    public void TheListIsDeduplicatedInOrder()
    {
        var tomorrow = MarketEventTestData.Event(
            id: "cpi-2026-03-12", moment: Now.AddDays(1),
            date: DateOnly.FromDateTime(Now.AddDays(1)));

        var kept = MarketEventWatchList.Prune(
            new[] { tomorrow.Id, "x-2027-01-01", tomorrow.Id }, new[] { tomorrow }, Now);

        Assert.Equal(new[] { tomorrow.Id, "x-2027-01-01" }, kept);
    }
}
