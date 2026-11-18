using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Alt+Shift+D: "Events on this bar" reads the level Off, names each event's long name, time,
/// importance, placement and source, and says what price did over the bar. No events, no section.
/// </summary>
public class MarketEventDetailTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheDetailSectionNamesEveryFactAndThePriceChange()
    {
        var bar = MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc), open: 100, close: 102);
        var e = MarketEventTestData.Event(
            longName: "Consumer Price Index",
            importance: MarketEventImportance.High,
            moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0, MarketEventPlacement.BeforeTheOpen) },
            0,
            bar,
            MarketEventKindFilter.All,
            MarketType.Stock);

        Assert.Contains("Events on this bar", said);
        Assert.Contains("Consumer Price Index", said);
        Assert.Contains("high importance", said);
        Assert.Contains("before the open", said);
        Assert.Contains("Test source", said);
        Assert.Contains("Price rose 2.0% over this bar", said);
    }

    [Fact]
    public void AFallingBarSaysPriceFell()
    {
        var bar = MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc), open: 100, close: 99);
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains("Price fell 1.0% over this bar", said);
    }

    [Fact]
    public void AFlatBarSaysPriceWasFlat()
    {
        var bar = MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc), open: 100, close: 100);
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains("Price was flat over this bar", said);
    }

    [Fact]
    public void NoEventsMeansNoSection()
    {
        var bar = MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("", MarketEventSpeech.Detail(
            Array.Empty<MarketEventBarPlacement>(), 0, bar, MarketEventKindFilter.All, MarketType.Crypto));
    }

    [Fact]
    public void TheDetailKeyReadsEventsWithTheLevelOff()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc), open: 100, close: 101),
        };
        var e = MarketEventTestData.Event(
            longName: "Consumer Price Index",
            moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var state = MarketEventTestData.State(bars, timeframe: "1d", index: 1, level: "off", withCandles: true);
        var bus = new SpyEventBus();
        var service = new BarDetailService(
            bus,
            patterns: null,
            analyzer: null,
            marketEvents: new MarketEventBarMapCache(MarketEventTestData.Calendar(e)));

        service.AnnounceDetails(state);

        var announcement = bus.Log.OfType<AnnouncementEvent>().LastOrDefault();
        Assert.NotNull(announcement);
        Assert.Contains("Events on this bar", announcement!.Message);
        Assert.Contains("Consumer Price Index", announcement.Message);
    }

    [Fact]
    public void TheDetailKeySaysNothingAboutEventsWhenThereAreNone()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };
        var state = MarketEventTestData.State(bars, timeframe: "1d", index: 1, level: "all", withCandles: true);
        var bus = new SpyEventBus();
        var service = new BarDetailService(
            bus,
            patterns: null,
            analyzer: null,
            marketEvents: new MarketEventBarMapCache(MarketEventTestData.Calendar()));

        service.AnnounceDetails(state);

        var announcement = bus.Log.OfType<AnnouncementEvent>().LastOrDefault();
        Assert.NotNull(announcement);
        Assert.DoesNotContain("Events on this bar", announcement!.Message);
    }
}
