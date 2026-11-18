using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests;

/// <summary>
/// The events worked out by rule: third-Friday expiries with the Good Friday move, crypto's last
/// Friday at 08:00 UTC, the exchange holidays (Juneteenth from 2022) and the four halvings.
/// </summary>
public class MarketEventRuleTests
{
    [Fact]
    public void April2025sStockExpiryIsThursdayThe17thBecauseThe18thIsGoodFriday()
    {
        var april = MarketEventRules.StockExpiries(2025).Single(e => e.Date.Month == 4);

        Assert.Equal(new DateOnly(2025, 4, 17), april.Date);
        Assert.Equal(DayOfWeek.Thursday, april.Date.DayOfWeek);
        Assert.Equal("options expiry", april.SpokenName);
    }

    [Fact]
    public void AQuarterlyStockExpiryIsMediumAndAPlainOneIsLow()
    {
        var expiries = MarketEventRules.StockExpiries(2025);

        Assert.Equal(MarketEventImportance.Medium, expiries.Single(e => e.Date.Month == 3).Importance);
        Assert.Equal("quarterly options expiry", expiries.Single(e => e.Date.Month == 3).SpokenName);
        Assert.Equal(MarketEventImportance.Low, expiries.Single(e => e.Date.Month == 4).Importance);
    }

    [Fact]
    public void StockExpiriesTouchStocksIndexesAndOptionsOnly()
    {
        var expiry = MarketEventRules.StockExpiries(2025)[0];

        Assert.Equal(
            new[] { MarketType.Stock, MarketType.Index, MarketType.Options }.OrderBy(x => x),
            expiry.Scope.OrderBy(x => x));
        Assert.Equal(MarketEventPrecision.DayOnly, expiry.Precision);
    }

    [Fact]
    public void TheJune2025CryptoExpiryIsFridayThe27thAt0800Utc()
    {
        var june = MarketEventRules.CryptoExpiries(2025).Single(e => e.Date.Month == 6);

        Assert.Equal(new DateOnly(2025, 6, 27), june.Date);
        Assert.Equal(new DateTime(2025, 6, 27, 8, 0, 0, DateTimeKind.Utc), june.MomentUtc);
        Assert.Equal(MarketEventPrecision.Exact, june.Precision);
        Assert.Equal("quarterly crypto options expiry", june.SpokenName);
        Assert.Equal(MarketEventImportance.Medium, june.Importance);
        Assert.Equal(new[] { MarketType.Crypto }, june.Scope);
    }

    [Fact]
    public void JuneteenthIsAbsentIn2021AndPresentFrom2022()
    {
        Assert.DoesNotContain(MarketEventRules.Holidays(2021), e => e.SpokenName == "Juneteenth");

        var juneteenth = MarketEventRules.Holidays(2022).Single(e => e.SpokenName == "Juneteenth");
        Assert.Equal(MarketEventKind.Holiday, juneteenth.Kind);
        Assert.Equal(MarketEventImportance.Low, juneteenth.Importance);
        Assert.Equal(MarketEventPrecision.DayOnly, juneteenth.Precision);
    }

    [Fact]
    public void HolidaysAreClosuresOfStocksIndexesAndOptions()
    {
        var thanksgiving = MarketEventRules.Holidays(2025).Single(e => e.SpokenName == "Thanksgiving");

        Assert.Equal(new DateOnly(2025, 11, 27), thanksgiving.Date);
        Assert.Equal(
            new[] { MarketType.Stock, MarketType.Index, MarketType.Options }.OrderBy(x => x),
            thanksgiving.Scope.OrderBy(x => x));
    }

    [Fact]
    public void ThereAreFourHalvingsAndTheyTouchOnlyCrypto()
    {
        var halvings = MarketEventRules.Halvings();

        Assert.Equal(4, halvings.Count);
        Assert.Equal(
            new[] { new DateOnly(2012, 11, 28), new DateOnly(2016, 7, 9), new DateOnly(2020, 5, 11), new DateOnly(2024, 4, 20) },
            halvings.Select(h => h.Date).OrderBy(d => d));
        Assert.All(halvings, h =>
        {
            Assert.Equal(MarketEventKind.Halving, h.Kind);
            Assert.Equal(MarketEventImportance.High, h.Importance);
            Assert.Equal(new[] { MarketType.Crypto }, h.Scope);
        });
    }

    [Fact]
    public void AStockYearCarriesNoCryptoExpiryOrHalving()
    {
        var year = MarketEventRules.ForYear(2024, MarketType.Stock);

        Assert.DoesNotContain(year, e => e.Kind == MarketEventKind.Halving);
        Assert.DoesNotContain(year, e => e.SpokenName.Contains("crypto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ACryptoYearCarriesNoStockExpiryOrHoliday()
    {
        var year = MarketEventRules.ForYear(2024, MarketType.Crypto);

        Assert.DoesNotContain(year, e => e.Kind == MarketEventKind.Holiday);
        Assert.DoesNotContain(year, e => e.SpokenName == "options expiry");
        Assert.Contains(year, e => e.Kind == MarketEventKind.Halving);
    }

    [Fact]
    public void TheCalendarReturnsAnEmptyListRatherThanNullWhenThereIsNothing()
    {
        var calendar = new MarketEventCalendar(Array.Empty<MarketEvent>());

        var events = calendar.Between(
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            MarketType.Crypto);

        Assert.NotNull(events);
        Assert.Empty(events);
    }
}
