using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests;

/// <summary>The words heard on a bar: one event, two, a crowd, a holiday, and "time not given".</summary>
public class MarketEventSpeechTests
{
    private static string Say(
        int barSeconds,
        params MarketEventBarPlacement[] placements)
        => Say(barSeconds, MarketType.Crypto, placements);

    private static string Say(
        int barSeconds,
        MarketType market,
        params MarketEventBarPlacement[] placements)
        => MarketEventSpeech.Clause(
            placements,
            barSeconds,
            MarketEventLevel.All,
            MarketEventKindFilter.All,
            market);

    [Fact]
    public void OneExactEventIsItsNameThenTheTimeOnTheUsersClock()
    {
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

        string said = Say(1800, MarketEventTestData.Placed(e, 0));

        Assert.Equal($"CPI {SpeechTimeFormatter.FormatTime(e.MomentUtc)}", said);
    }

    [Fact]
    public void APlacementWordFollowsTheTime()
    {
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

        string said = Say(1800, MarketEventTestData.Placed(e, 0, MarketEventPlacement.BeforeTheOpen));

        Assert.EndsWith(", before the open", said);
        Assert.StartsWith("CPI ", said);
    }

    [Fact]
    public void AHalvingSaysTimeNotGiven()
    {
        var halving = MarketEventTestData.Event(
            kind: MarketEventKind.Halving,
            spoken: "Bitcoin halving",
            longName: "Bitcoin halving",
            importance: MarketEventImportance.High,
            moment: new DateTime(2024, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2024, 4, 20),
            scope: MarketType.Crypto);

        Assert.Equal("Bitcoin halving, time not given", Say(86400, MarketEventTestData.Placed(halving, 0)));
    }

    [Fact]
    public void AStockExpiryDoesNotSayTimeNotGiven()
    {
        var expiry = MarketEventTestData.Event(
            kind: MarketEventKind.Expiry,
            spoken: "options expiry",
            importance: MarketEventImportance.Low,
            moment: new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2024, 3, 15),
            scope: new[] { MarketType.Stock, MarketType.Index, MarketType.Options });

        Assert.Equal("options expiry", Say(86400, MarketType.Stock, MarketEventTestData.Placed(expiry, 0)));
    }

    [Fact]
    public void APre1997ReleaseSaysTimeNotGiven()
    {
        var cpi = MarketEventTestData.Event(
            importance: MarketEventImportance.High,
            moment: new DateTime(1996, 12, 12, 12, 30, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(1996, 12, 12));

        Assert.Equal("CPI, time not given", Say(86400, MarketEventTestData.Placed(cpi, 0)));
    }

    [Fact]
    public void AHolidayIsSaidAsMarketClosedWithItsWeekday()
    {
        var thanksgiving = MarketEventTestData.Event(
            kind: MarketEventKind.Holiday,
            spoken: "Thanksgiving",
            longName: "US market closed for Thanksgiving",
            importance: MarketEventImportance.Low,
            moment: new DateTime(2024, 11, 28, 0, 0, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2024, 11, 28),
            scope: new[] { MarketType.Stock, MarketType.Index, MarketType.Options });

        string said = Say(86400, MarketType.Stock, MarketEventTestData.Placed(thanksgiving, 0, "came out Thursday"));

        Assert.Equal("market closed Thursday, Thanksgiving", said);
    }

    [Fact]
    public void ThreeEventsShowTheTwoMostImportantThenPlusOneMoreEvent()
    {
        var cpi = MarketEventTestData.Event(
            id: "cpi", importance: MarketEventImportance.High,
            moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var ppi = MarketEventTestData.Event(
            id: "ppi", spoken: "PPI", importance: MarketEventImportance.Medium,
            moment: new DateTime(2024, 3, 12, 12, 31, 0, DateTimeKind.Utc));
        var low = MarketEventTestData.Event(
            id: "low", kind: MarketEventKind.Expiry, spoken: "options expiry",
            importance: MarketEventImportance.Low,
            moment: new DateTime(2024, 3, 12, 12, 32, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2024, 3, 12));

        string said = Say(1800,
            MarketEventTestData.Placed(cpi, 0),
            MarketEventTestData.Placed(ppi, 0),
            MarketEventTestData.Placed(low, 0));

        Assert.Contains("CPI", said);
        Assert.Contains("PPI", said);
        Assert.DoesNotContain("options expiry", said);
        Assert.EndsWith("plus 1 more event", said);
    }

    [Fact]
    public void FourEventsSayPlusTwoMoreEvents()
    {
        var a = MarketEventTestData.Event(id: "a", spoken: "CPI",
            moment: new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc));
        var b = MarketEventTestData.Event(id: "b", kind: MarketEventKind.Fed, spoken: "Fed decision",
            moment: new DateTime(2024, 3, 12, 1, 0, 0, DateTimeKind.Utc));
        var c = MarketEventTestData.Event(id: "c", spoken: "PPI", importance: MarketEventImportance.Medium,
            moment: new DateTime(2024, 3, 12, 2, 0, 0, DateTimeKind.Utc));
        var d = MarketEventTestData.Event(id: "d", spoken: "GDP", importance: MarketEventImportance.Medium,
            moment: new DateTime(2024, 3, 12, 3, 0, 0, DateTimeKind.Utc));

        string said = Say(1800,
            MarketEventTestData.Placed(a, 0), MarketEventTestData.Placed(b, 0),
            MarketEventTestData.Placed(c, 0), MarketEventTestData.Placed(d, 0));

        Assert.EndsWith("plus 2 more events", said);
    }

    [Fact]
    public void TwoEventsAreBothSaidWithNoPlusClause()
    {
        var cpi = MarketEventTestData.Event(id: "cpi",
            moment: new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc));
        var ppi = MarketEventTestData.Event(id: "ppi", spoken: "PPI",
            importance: MarketEventImportance.Medium,
            moment: new DateTime(2024, 3, 12, 1, 0, 0, DateTimeKind.Utc));

        string said = Say(1800, MarketEventTestData.Placed(cpi, 0), MarketEventTestData.Placed(ppi, 0));

        Assert.Contains("CPI", said);
        Assert.Contains("PPI", said);
        Assert.DoesNotContain("plus", said);
    }

    [Fact]
    public void AWeeklyBarCarriesEachNameWithItsWeekday()
    {
        var cpi = MarketEventTestData.Event(id: "cpi", spoken: "CPI",
            moment: new DateTime(2024, 3, 11, 12, 30, 0, DateTimeKind.Utc)); // Monday
        var fed = MarketEventTestData.Event(id: "fed", kind: MarketEventKind.Fed, spoken: "Fed decision",
            moment: new DateTime(2024, 3, 13, 18, 0, 0, DateTimeKind.Utc)); // Wednesday
        var ppi = MarketEventTestData.Event(id: "ppi", spoken: "PPI",
            importance: MarketEventImportance.Medium,
            moment: new DateTime(2024, 3, 15, 12, 30, 0, DateTimeKind.Utc)); // Friday

        string said = Say(604800,
            MarketEventTestData.Placed(cpi, 0),
            MarketEventTestData.Placed(fed, 0),
            MarketEventTestData.Placed(ppi, 0));

        Assert.Equal("CPI Monday, Fed decision Wednesday, plus 1 more event", said);
    }

    [Fact]
    public void AnEmptyBarSaysNothing()
        => Assert.Equal("", Say(1800));
}
