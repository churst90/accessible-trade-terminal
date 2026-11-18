using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The placement rule, one test per numbered rule in the handoff. These call the pure function
/// with explicit bars, bar length, zone and "now" — no clock, no store.
/// </summary>
public class MarketEventPlacementTests
{
    private static readonly TimeZoneInfo Eastern = ExchangeTime.UsEastern;
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MarketEventBarPlacement PlaceOne(
        MarketEvent e,
        IReadOnlyList<Ohlcv> bars,
        int barSeconds,
        TimeZoneInfo zone,
        DateTime? now = null)
        => Assert.Single(MarketEventPlacement.Place(new[] { e }, bars, barSeconds, zone, now ?? Now));

    // ── Rule 1: the bar whose span holds the moment; its open is inside it ──

    [Fact]
    public void AnExactEventAt830GoesOnTheBarThatOpensAt830NotTheOneBefore()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 12, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 13, 0, 0, DateTimeKind.Utc)),
        };
        var cpi = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

        var placement = PlaceOne(cpi, bars, 1800, TimeZoneInfo.Utc);

        Assert.Equal(1, placement.BarIndex);
        Assert.Null(placement.Placement);
    }

    [Fact]
    public void AnExactEventJustAfterAOneHourBarsOpenGoesOnThatBar()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 8, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 9, 0, 0, DateTimeKind.Utc)),
        };
        var cpi = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 8, 30, 0, DateTimeKind.Utc));

        var placement = PlaceOne(cpi, bars, 3600, TimeZoneInfo.Utc);

        Assert.Equal(0, placement.BarIndex);
    }

    [Fact]
    public void AStockChartsEventDateFollowsNewYorkDaylightSaving()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };
        // 04:30Z is 00:30 on the 12th in New York in March (EDT, UTC-4) but 23:30 on the 11th
        // under a fixed UTC-5. The daily bar's date is the give-away.
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 4, 30, 0, DateTimeKind.Utc));

        var placement = PlaceOne(e, bars, 86400, Eastern);

        Assert.Equal(1, placement.BarIndex);
    }

    // ── Rule 2: same day, outside the session ──

    [Fact]
    public void AStock30MinuteChartWhoseFirstBarIs930PutsCpiOnItBeforeTheOpen()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 13, 30, 0, DateTimeKind.Utc)), // 09:30 ET
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 14, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 14, 30, 0, DateTimeKind.Utc)),
        };
        var cpi = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

        var placement = PlaceOne(cpi, bars, 1800, Eastern);

        Assert.Equal(0, placement.BarIndex);
        Assert.Equal(MarketEventPlacement.BeforeTheOpen, placement.Placement);
    }

    [Fact]
    public void AnEventAt1605GoesOnThe1530BarAfterTheClose()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 20, 0, 0, DateTimeKind.Utc)),  // 15:00 ET
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 20, 30, 0, DateTimeKind.Utc)), // 15:30 ET
            MarketEventTestData.Bar(new DateTime(2024, 3, 13, 13, 30, 0, DateTimeKind.Utc)), // next session
        };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 21, 5, 0, DateTimeKind.Utc));

        var placement = PlaceOne(e, bars, 1800, Eastern);

        Assert.Equal(1, placement.BarIndex);
        Assert.Equal(MarketEventPlacement.AfterTheClose, placement.Placement);
    }

    // ── Rule 3: daily bars use the chart's own date ──

    [Fact]
    public void ACryptoDailyChartUsesTheUtcDate()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 4, 19, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 4, 20, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 4, 21, 0, 0, 0, DateTimeKind.Utc)),
        };
        // 01:00Z on the 20th is still the evening of the 19th in New York; UTC is the crypto day.
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 4, 20, 1, 0, 0, DateTimeKind.Utc));

        var placement = PlaceOne(e, bars, 86400, TimeZoneInfo.Utc);

        Assert.Equal(1, placement.BarIndex);
    }

    // ── Rule 4: weekly and monthly bars hold the date's span ──

    [Fact]
    public void AWeeklyBarHoldsEveryEventOfItsWeek()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 18, 0, 0, 0, DateTimeKind.Utc)),
        };
        var tuesday = MarketEventTestData.Event(
            id: "tuesday", moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var thursday = MarketEventTestData.Event(
            id: "thursday", moment: new DateTime(2024, 3, 14, 12, 30, 0, DateTimeKind.Utc));

        var placements = MarketEventPlacement.Place(
            new[] { tuesday, thursday }, bars, 604800, TimeZoneInfo.Utc, Now);

        Assert.All(placements, p => Assert.Equal(0, p.BarIndex));
    }

    // ── Rule 5: a date with no bar goes on the next one ──

    [Fact]
    public void ASaturdayEventGoesOnMondaysBarAsCameOutSaturday()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 18, 0, 0, 0, DateTimeKind.Utc)),
        };
        var saturday = MarketEventTestData.Event(moment: new DateTime(2024, 3, 16, 12, 0, 0, DateTimeKind.Utc));

        var placement = PlaceOne(saturday, bars, 86400, TimeZoneInfo.Utc);

        Assert.Equal(1, placement.BarIndex);
        Assert.Equal("came out Saturday", placement.Placement);
    }

    [Fact]
    public void AnEventInAGapBetweenDailyBarsIsNeverDropped()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 14, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 18, 0, 0, 0, DateTimeKind.Utc)),
        };
        var saturday = MarketEventTestData.Event(moment: new DateTime(2024, 3, 16, 12, 0, 0, DateTimeKind.Utc));

        var placement = PlaceOne(saturday, bars, 86400, TimeZoneInfo.Utc);

        Assert.True(placement.IsPlaced);
        Assert.Equal(1, placement.BarIndex);
    }

    [Fact]
    public void ADayOnlyEventOnAnIntradayChartGoesOnTheDaysFirstBar()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 10, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 10, 30, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 11, 0, 0, DateTimeKind.Utc)),
        };
        var expiry = MarketEventTestData.Event(
            kind: MarketEventKind.Expiry,
            spoken: "options expiry",
            importance: MarketEventImportance.Low,
            moment: new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2024, 3, 12),
            scope: MarketType.Stock);

        var placement = PlaceOne(expiry, bars, 1800, TimeZoneInfo.Utc);

        Assert.Equal(0, placement.BarIndex);
        Assert.Null(placement.Placement);
    }

    // ── Rule 6 is covered by the day-only test above. Rule 7: the last bar and "now" ──

    [Fact]
    public void AnEventInsideTheLastBarButLaterThanNowIsScheduled()
    {
        var bars = new[] { MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)) };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var now = new DateTime(2024, 3, 12, 10, 0, 0, DateTimeKind.Utc);

        var placement = PlaceOne(e, bars, 86400, TimeZoneInfo.Utc, now);

        Assert.Equal(0, placement.BarIndex);
        Assert.Equal(MarketEventPlacement.Scheduled, placement.Placement);
    }

    [Fact]
    public void AnEventAfterTheLastBarsSpanIsNotPlaced()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 18, 0, 0, 0, DateTimeKind.Utc)),
        };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 20, 12, 0, 0, DateTimeKind.Utc));

        var placement = PlaceOne(e, bars, 86400, TimeZoneInfo.Utc);

        Assert.False(placement.IsPlaced);
        Assert.Equal(MarketEventPlacement.NotPlacedWord, placement.Placement);
    }

    // ── Rule 8: before the first bar ──

    [Fact]
    public void AnEventBeforeTheFirstBarIsNotPlaced()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 13, 0, 0, 0, DateTimeKind.Utc)),
        };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 11, 12, 0, 0, DateTimeKind.Utc));

        var placement = PlaceOne(e, bars, 86400, TimeZoneInfo.Utc);

        Assert.False(placement.IsPlaced);
    }

    // ── Placement must not rescan the bars once per event ──

    [Fact]
    public void PlacingEverySeedEventComputesEachBarsDateOnce()
    {
        var events = MarketEventSeed.Load().Events;

        var daily = ManyBars(new DateTime(2000, 1, 3, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromDays(1), 5000);
        MarketEventPlacement.BarDateCalls = 0;
        MarketEventPlacement.Place(events, daily, 86400, TimeZoneInfo.Utc, Now);
        Assert.True(
            MarketEventPlacement.BarDateCalls <= daily.Length + events.Count,
            $"daily: {MarketEventPlacement.BarDateCalls} BarDate calls for {daily.Length} bars and {events.Count} events");

        var hourly = ManyBars(new DateTime(2000, 1, 3, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(1), 5000);
        MarketEventPlacement.BarDateCalls = 0;
        MarketEventPlacement.Place(events, hourly, 3600, TimeZoneInfo.Utc, Now);
        Assert.True(
            MarketEventPlacement.BarDateCalls <= hourly.Length + events.Count,
            $"hourly: {MarketEventPlacement.BarDateCalls} BarDate calls for {hourly.Length} bars and {events.Count} events");
    }

    private static Ohlcv[] ManyBars(DateTime startUtc, TimeSpan step, int count)
    {
        var bars = new Ohlcv[count];
        for (int i = 0; i < count; i++)
            bars[i] = MarketEventTestData.Bar(startUtc.AddTicks(step.Ticks * i));
        return bars;
    }

    // ── Replay: the data is a growing prefix, so rule 7 keeps later events out ──

    [Fact]
    public void AReplayPrefixDoesNotPlaceEventsOnBarsThatHaveNotBeenRevealed()
    {
        var all = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 13, 0, 0, 0, DateTimeKind.Utc)),
        };
        var revealed = all.Take(2).ToArray();
        var later = MarketEventTestData.Event(moment: new DateTime(2024, 3, 13, 12, 0, 0, DateTimeKind.Utc));
        var cache = new MarketEventBarMapCache(MarketEventTestData.Calendar(later));

        var placements = cache.For(MarketEventTestData.State(revealed, timeframe: "1d"), Now);

        Assert.All(placements, p => Assert.False(p.IsPlaced));
    }
}
