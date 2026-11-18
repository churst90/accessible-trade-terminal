using System.Globalization;
using AccessibleTrader.Core.Services.MarketPulse;

namespace AccessibleTrader.Tests;

/// <summary>
/// One sentence, in list order, with the exact words a screen-reader user hears. The examples
/// here are the handoff's examples; a wording change is a product change, not a test fix.
/// </summary>
public class MarketPulseWordingTests
{
    private static readonly DateTime Monday = new(2026, 11, 23, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Friday = new(2026, 11, 20);

    private static MarketPulseQuote Change(string name, double change, int? minutes = null)
        => new(name, MarketPulseKind.Change, Change: change, MinutesOld: minutes);

    private static MarketPulseQuote Yield(string name, double level, double basisPoints, int? minutes = null)
        => new(name, MarketPulseKind.Yield, Change: basisPoints, YieldLevel: level,
            MinutesOld: minutes, IsYield: true);

    private static MarketPulseQuote Closed(string name, double change, DateOnly date)
        => new(name, MarketPulseKind.Closed, Change: change, SessionDate: date);

    private static MarketPulseQuote ClosedYield(string name, double level, double basisPoints, DateOnly date)
        => new(name, MarketPulseKind.Closed, Change: basisPoints, YieldLevel: level,
            SessionDate: date, IsYield: true);

    private static MarketPulseQuote Unavailable(string name)
        => new(name, MarketPulseKind.Unavailable);

    [Fact]
    public void TheHandoffsWholeSentenceComesOutExactly()
    {
        var quotes = new[]
        {
            Change("S&P", 0.4),
            Change("Nasdaq", 0.6),
            Change("Bitcoin", -1.2),
            Change("Ethereum", -2.1),
            Yield("ten-year", 4.31, 3),
            Change("dollar index", 0),
            Change("gold", 0.2, minutes: 4),
            Unavailable("oil"),
            Closed("VIX", 2.1, Friday),
        };

        Assert.Equal(
            "S&P up 0.4, Nasdaq up 0.6, Bitcoin down 1.2, Ethereum down 2.1, "
          + "ten-year 4.31, up 3 basis points, dollar index flat, gold up 0.2, 4 minutes old, "
          + "oil unavailable, VIX closed, up 2.1 Friday.",
            MarketPulseWording.Sentence(quotes, Monday));
    }

    [Fact]
    public void OneBasisPointIsSingular()
    {
        Assert.Equal("ten-year 4.31, up 1 basis point.",
            MarketPulseWording.Sentence(new[] { Yield("ten-year", 4.31, 1) }, Monday));
    }

    [Fact]
    public void OneMinuteOldIsSingular()
    {
        Assert.Equal("gold up 0.2, 1 minute old.",
            MarketPulseWording.Sentence(new[] { Change("gold", 0.2, minutes: 1) }, Monday));
    }

    [Fact]
    public void AFlatQuoteAtFourHundredthsEitherWayIsFlat()
    {
        Assert.Equal("dollar index flat.",
            MarketPulseWording.Sentence(new[] { Change("dollar index", 0.04) }, Monday));
        Assert.Equal("dollar index flat.",
            MarketPulseWording.Sentence(new[] { Change("dollar index", -0.04) }, Monday));
        Assert.Equal("dollar index up 0.1.",
            MarketPulseWording.Sentence(new[] { Change("dollar index", 0.05) }, Monday));
    }

    [Fact]
    public void AFlatYieldKeepsItsLevel()
    {
        Assert.Equal("ten-year 4.31, flat.",
            MarketPulseWording.Sentence(new[] { Yield("ten-year", 4.31, 0) }, Monday));
    }

    [Fact]
    public void AClosedYieldKeepsItsLevelAndBasisPoints()
    {
        Assert.Equal("ten-year closed, 4.31, up 3 basis points Friday.",
            MarketPulseWording.Sentence(new[] { ClosedYield("ten-year", 4.31, 3, Friday) }, Monday));
    }

    [Fact]
    public void ClosedTodayAndYesterdayUseThoseWords()
    {
        var now = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc); // Wednesday, 10:00 Eastern
        Assert.Equal("S&P closed, down 0.3 today.",
            MarketPulseWording.Sentence(
                new[] { Closed("S&P", -0.3, new DateOnly(2026, 11, 18)) }, now));
        Assert.Equal("S&P closed, down 0.3 yesterday.",
            MarketPulseWording.Sentence(
                new[] { Closed("S&P", -0.3, new DateOnly(2026, 11, 17)) }, now));
        Assert.Equal("S&P closed, down 0.3 Friday.",
            MarketPulseWording.Sentence(
                new[] { Closed("S&P", -0.3, new DateOnly(2026, 11, 13)) }, now));
    }

    [Fact]
    public void WordingIsInvariantUnderGermanCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("S&P up 0.4.",
                MarketPulseWording.Sentence(new[] { Change("S&P", 0.4) }, Monday));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AnEmptyListSaysHowToFillIt()
    {
        Assert.Equal(
            "Your Pulse list is empty. Add markets to it in the watchlist screen, Alt+M.",
            MarketPulseWording.Sentence(Array.Empty<MarketPulseQuote>(), Monday));
    }
}
