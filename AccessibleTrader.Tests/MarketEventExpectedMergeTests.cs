using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The calendar merges Kalshi's expected figure into a copy of the event; the seed objects are
/// never mutated, and a release with no stored figure can still carry an expected one.
/// </summary>
public class MarketEventExpectedMergeTests
{
    private sealed class FakeExpectedFeed : IMarketEventExpectedFeed
    {
        private readonly Dictionary<(string Source, DateOnly Date, string Figure), double> _values = new();

        public int Version { get; private set; }

        public FakeExpectedFeed Add(string source, DateOnly date, string figure, double value)
        {
            _values[(source, date, figure)] = value;
            Version++;
            return this;
        }

        public double? ExpectedFor(string source, DateOnly releaseDate, string figureName)
            => _values.TryGetValue((source, releaseDate, figureName), out double value)
                ? value
                : null;

        public void RequestRefresh(WorkspaceState state) { }
    }

    [Fact]
    public void BetweenReturnsACopyWithTheExpectedFigureAndLeavesTheSeedAlone()
    {
        var seed = MarketEventTestData.Event(
            id: "cpi-2026-07-14", moment: new DateTime(2026, 7, 14, 12, 30, 0, DateTimeKind.Utc),
            date: new DateOnly(2026, 7, 14),
            figures: new MarketEventFigures(new[]
            {
                new MarketEventFigure("CPI", 0.4, 0.3,
                    MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
                new MarketEventFigure("core CPI", 0.0, 0.2,
                    MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
            }));
        var expected = new FakeExpectedFeed()
            .Add("cpi", new DateOnly(2026, 7, 14), "CPI", 0.3)
            .Add("cpi", new DateOnly(2026, 7, 14), "core CPI", 0.2);
        var calendar = new MarketEventCalendar(new[] { seed }, expectedFeed: expected);

        var merged = calendar.Between(
            new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            MarketType.Crypto).Single();

        Assert.Equal(0.3, merged.Figures!.Figures.Single(f => f.Name == "CPI").Expected);
        Assert.Equal(0.2, merged.Figures.Figures.Single(f => f.Name == "core CPI").Expected);
        Assert.Null(seed.Figures!.Figures.Single(f => f.Name == "CPI").Expected);
    }

    [Fact]
    public void AReleaseWithNoFiguresGainsTheExpectedOnlyFigure()
    {
        var seed = MarketEventTestData.Event(
            id: "cpi-2026-07-14", moment: new DateTime(2026, 7, 14, 12, 30, 0, DateTimeKind.Utc),
            date: new DateOnly(2026, 7, 14));
        var expected = new FakeExpectedFeed()
            .Add("cpi", new DateOnly(2026, 7, 14), "CPI", 0.3);
        var calendar = new MarketEventCalendar(new[] { seed }, expectedFeed: expected);

        var merged = calendar.Between(
            new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            MarketType.Crypto).Single();

        var figure = Assert.Single(merged.Figures!.Figures);
        Assert.Equal("CPI", figure.Name);
        Assert.Null(figure.Actual);
        Assert.Equal(0.3, figure.Expected);
        Assert.Null(seed.Figures);
    }
}
