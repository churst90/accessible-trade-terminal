using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// An upcoming release that carries only Kalshi's expected figure has NOT published its figure.
/// The merge adds an expected-only row, so "has a figures object" no longer means "the figure
/// arrived" — the release minute must still ask FRED for it and still say it is waiting.
/// </summary>
public class MarketEventExpectedOnlyTests
{
    private static readonly DateTime T = new(2026, 12, 10, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 12, 10);

    private sealed class OneExpected : IMarketEventExpectedFeed
    {
        public int Version => 1;

        public double? ExpectedFor(string source, DateOnly releaseDate, string figureName)
            => source == "cpi" && releaseDate == Day && figureName == "CPI" ? -0.1 : null;

        public void RequestRefresh(WorkspaceState state) { }
    }

    private static MarketEvent MergedUpcomingCpi()
    {
        var bare = MarketEventTestData.Event(
            id: "cpi-2026-12-10", spoken: "CPI", moment: T, date: Day,
            scope: new[] { MarketType.Crypto }) with { Source = "FRED release 10, fetched 2026-11-18" };
        var calendar = new MarketEventCalendar(new[] { bare }, expectedFeed: new OneExpected());
        var merged = calendar.Between(T.AddDays(-1), T.AddDays(1), MarketType.Crypto).Single();
        Assert.Equal(-0.1, merged.Figures!.Figures.Single(f => f.Name == "CPI").Expected);
        Assert.Null(merged.Figures.Figures.Single(f => f.Name == "CPI").Actual);
        return merged;
    }

    [Fact]
    public void AnExpectedOnlyReleaseStillAsksForItsFigureAfterTheMinute()
    {
        var e = MergedUpcomingCpi();

        var due = MarketEventNoticePlanner.Due(
            T.AddMinutes(2).AddSeconds(5), new[] { e }, new[] { 15, 1 },
            new[] { MarketType.Crypto }, Array.Empty<MarketType>(),
            MarketEventLevel.All, MarketEventKindFilter.All,
            Array.Empty<string>(), fredKeyAvailable: true);

        Assert.Contains(due, n => n.Kind == MarketEventNoticeKind.Figure);
    }

    [Fact]
    public void AnExpectedOnlyReleaseHasNoPublishedFigure()
    {
        var e = MergedUpcomingCpi();

        Assert.False(MarketEventFigures.Published(e));
        Assert.True(MarketEventFigures.Published(e with
        {
            Figures = new MarketEventFigures(new[]
            {
                new MarketEventFigure("CPI", 0.2, 0.3,
                    MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter, -0.1),
            }),
        }));
    }

    [Fact]
    public void ThePastCalendarLineSaysExpectedOnceWhenTheActualIsMissing()
    {
        var e = MergedUpcomingCpi();

        string line = MarketEventCalendarWording.EventLine(e, T.AddHours(3));

        Assert.Equal(1, CountOf(line, "expected"));
        Assert.DoesNotContain("unavailable", line);
    }

    private static int CountOf(string text, string word)
    {
        int count = 0;
        for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(word, at + word.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
