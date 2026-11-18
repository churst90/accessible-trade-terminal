using AccessibleTrader.Core.Services.MarketEvents;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// The pure Kalshi rules: candidate tickers, the close-date guard, the quote rule for a candle
/// list and a market-list row, and the median that turns a ladder into one expected figure.
/// </summary>
public class MarketEventKalshiExpectedFiguresTests
{
    private static KalshiStrikeProbability Q(double strike, double probability)
        => new(strike, probability);

    [Fact]
    public void OneStepStrikesGiveTheSecondStrike()
    {
        double? expected = KalshiExpectedFigures.Median(
            new[] { Q(0.1, 0.7), Q(0.2, 0.3) }, 0.1);

        Assert.Equal(0.2, expected);
    }

    [Fact]
    public void WideStrikesInterpolateAndRoundUp()
    {
        // c = 0.0 + 0.5 * (0.9 - 0.5) / (0.9 - 0.3) = 0.333…, and the grid is 0.1.
        double? expected = KalshiExpectedFigures.Median(
            new[] { Q(0.0, 0.9), Q(0.5, 0.3) }, 0.1);

        Assert.Equal(0.4, expected);
    }

    [Fact]
    public void ANegativeLadderRoundsUpTowardZero()
    {
        double? expected = KalshiExpectedFigures.Median(
            new[] { Q(-0.2, 0.82), Q(-0.1, 0.365) }, 0.1);

        Assert.Equal(-0.1, expected);
    }

    [Fact]
    public void PayrollsUseTheWholeThousandsGrid()
    {
        double? expected = KalshiExpectedFigures.Median(
            new[] { Q(100, 0.9), Q(125, 0.6), Q(150, 0.2) }, 1);

        Assert.Equal(132, expected);
    }

    [Fact]
    public void ALadderThatNeverCrossesHalfHasNoExpectedFigure()
    {
        Assert.Null(KalshiExpectedFigures.Median(
            new[] { Q(0.1, 0.8), Q(0.2, 0.7), Q(0.3, 0.6) }, 0.1));
    }

    [Fact]
    public void ASingleStrikeHasNoExpectedFigure()
    {
        Assert.Null(KalshiExpectedFigures.Median(new[] { Q(0.1, 0.7) }, 0.1));
    }

    [Fact]
    public void InputIsMadeMonotoneFromTheLowestStrikeUp()
    {
        // The raw ladder .9, .1, .8, .9 never crosses .5 without the running minimum.
        double? expected = KalshiExpectedFigures.Median(
            new[] { Q(0.1, 0.9), Q(0.2, 0.1), Q(0.3, 0.8), Q(0.4, 0.9) }, 0.1);

        Assert.Equal(0.2, expected);
    }

    [Fact]
    public void AHistoricCandleUsesTheCloseSpelling()
    {
        var candle = JObject.Parse("""
            {"end_period_ts": 1, "yes_bid": {"close": "0.4000"},
             "yes_ask": {"close": "0.5000"}, "price": {"close": null}}
            """);

        Assert.Equal(0.45, KalshiExpectedFigures.CandleProbability(candle));
    }

    [Fact]
    public void ALiveCandleUsesTheCloseDollarsSpelling()
    {
        var candle = JObject.Parse("""
            {"end_period_ts": 1, "yes_bid": {"close_dollars": "0.4000"},
             "yes_ask": {"close_dollars": "0.5000"}, "price": {"close_dollars": null}}
            """);

        Assert.Equal(0.45, KalshiExpectedFigures.CandleProbability(candle));
    }

    [Fact]
    public void AWideSpreadFallsBackToTheLastTrade()
    {
        var candle = JObject.Parse("""
            {"end_period_ts": 1, "yes_bid": {"close": "0.3000"},
             "yes_ask": {"close": "0.4100"}, "price": {"close": "0.3700"}}
            """);

        Assert.Equal(0.37, KalshiExpectedFigures.CandleProbability(candle));
    }

    [Fact]
    public void AnEmptyBookWithNoTradeIsNotAQuote()
    {
        var candle = JObject.Parse("""
            {"end_period_ts": 1, "yes_bid": {"close": "0.0000"},
             "yes_ask": {"close": "1.0000"}, "price": {"close": null}}
            """);

        Assert.Null(KalshiExpectedFigures.CandleProbability(candle));
    }

    [Fact]
    public void TheQuoteWalksBackToTheNewestUsableCandleBeforeTheRelease()
    {
        var moment = new DateTime(2024, 11, 13, 13, 30, 0, DateTimeKind.Utc);
        var candles = new[]
        {
            JObject.Parse("""
                {"end_period_ts": 1731508200, "yes_bid": {"close": "0.8000"},
                 "yes_ask": {"close": "0.9000"}, "price": {"close": null}}
                """),
            JObject.Parse("""
                {"end_period_ts": 1731501000, "yes_bid": {"close": "0.0000"},
                 "yes_ask": {"close": "1.0000"}, "price": {"close": null}}
                """),
            JObject.Parse("""
                {"end_period_ts": 1731497400, "yes_bid": {"close": "0.4000"},
                 "yes_ask": {"close": "0.5000"}, "price": {"close": null}}
                """),
        };

        Assert.Equal(0.45, KalshiExpectedFigures.ProbabilityAt(candles, moment));
    }

    [Fact]
    public void AMarketListRowUsesTheSameQuoteRule()
    {
        var narrow = JObject.Parse("""
            {"strike_type":"greater","floor_strike":0.2,"yes_bid_dollars":"0.5000",
             "yes_ask_dollars":"0.6000","last_price_dollars":"0.5500"}
            """);
        var wide = JObject.Parse("""
            {"strike_type":"greater","floor_strike":0.3,"yes_bid_dollars":"0.3000",
             "yes_ask_dollars":"0.4100","last_price_dollars":"0.3700"}
            """);

        Assert.Equal(0.55, KalshiExpectedFigures.MarketProbability(narrow));
        Assert.Equal(0.37, KalshiExpectedFigures.MarketProbability(wide));
    }

    [Fact]
    public void TheCandidateTickersAreNewestSeriesFirst()
    {
        Assert.Equal(
            new[] { "KXCPI-26JUN", "CPI-26JUN" },
            KalshiExpectedFigures.EventTickers("CPI", new DateOnly(2026, 7, 14)));
        Assert.Equal(
            new[] { "KXCPICORE-26JUN", "CPICORE-26JUN" },
            KalshiExpectedFigures.EventTickers("core CPI", new DateOnly(2026, 7, 14)));
    }

    [Fact]
    public void AJanuaryReleaseUsesThePreviousDecember()
    {
        Assert.Equal(
            new[] { "KXPAYROLLS-25DEC", "PAYROLLS-25DEC" },
            KalshiExpectedFigures.EventTickers("payrolls", new DateOnly(2026, 1, 9)));
    }

    [Fact]
    public void GdpUsesTheReleaseDateItself()
    {
        Assert.Equal(
            new[] { "KXGDP-26APR30", "GDP-26APR30" },
            KalshiExpectedFigures.EventTickers("GDP", new DateOnly(2026, 4, 30)));
        Assert.Equal(
            new[] { "KXGDP-24OCT30", "GDP-24OCT30" },
            KalshiExpectedFigures.EventTickers("GDP", new DateOnly(2024, 10, 30)));
    }

    [Fact]
    public void AMarketThirtyDaysOffTheReleaseIsIgnored()
    {
        Assert.False(KalshiExpectedFigures.CloseDateMatches(
            "2026-08-13T12:25:00Z", new DateOnly(2026, 7, 14)));
        Assert.True(KalshiExpectedFigures.CloseDateMatches(
            "2026-07-14T12:25:00Z", new DateOnly(2026, 7, 14)));
        Assert.True(KalshiExpectedFigures.CloseDateMatches(
            "2026-07-15T12:25:00Z", new DateOnly(2026, 7, 14)));
    }
}
