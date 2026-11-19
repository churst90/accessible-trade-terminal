using System.Globalization;
using AccessibleTrader.Core.Services.GoBar;

namespace AccessibleTrader.Tests;

/// <summary>
/// The pure reader for stage 6b: words in either order, one optional value, the two bare
/// shapes and the sentences a bad value gets. No chart, no bus, no services.
/// </summary>
public class GoBarParserTests
{
    // ── Either word order, for every verb ──────────────────────────────────

    [Theory]
    [InlineData("TF 4h", GoBarVerb.Timeframe, "4h")]
    [InlineData("4h TF", GoBarVerb.Timeframe, "4h")]
    [InlineData("GP BTC", GoBarVerb.Symbol, "BTC")]
    [InlineData("BTC GP", GoBarVerb.Symbol, "BTC")]
    [InlineData("SYM BTC", GoBarVerb.Symbol, "BTC")]
    [InlineData("BTC SYM", GoBarVerb.Symbol, "BTC")]
    [InlineData("PROV Kraken", GoBarVerb.Provider, "Kraken")]
    [InlineData("Kraken PROV", GoBarVerb.Provider, "Kraken")]
    [InlineData("IND rsi", GoBarVerb.Indicator, "rsi")]
    [InlineData("rsi IND", GoBarVerb.Indicator, "rsi")]
    [InlineData("ALERT 95000", GoBarVerb.PriceAlert, "95000")]
    [InlineData("95000 ALERT", GoBarVerb.PriceAlert, "95000")]
    [InlineData("GOTO 2024-03-01", GoBarVerb.GoTo, "2024-03-01")]
    [InlineData("2024-03-01 GOTO", GoBarVerb.GoTo, "2024-03-01")]
    [InlineData("FIND hammer", GoBarVerb.Find, "hammer")]
    [InlineData("hammer FIND", GoBarVerb.Find, "hammer")]
    public void EitherWordOrderNamesTheSameVerbAndValue(
        string typed, GoBarVerb expectedVerb, string expectedArgument)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.Argument, parsed.Kind);
        Assert.Equal(expectedVerb, parsed.Verb);
        Assert.Equal(expectedArgument, parsed.Argument);
    }

    [Theory]
    [InlineData("go to CPI", "CPI")]
    [InlineData("CPI go to", "CPI")]
    [InlineData("GOTO CPI", "CPI")]
    [InlineData("CPI GOTO", "CPI")]
    public void GoToIsAPhraseAtEitherEnd(string typed, string expected)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.Argument, parsed.Kind);
        Assert.Equal(GoBarVerb.GoTo, parsed.Verb);
        Assert.Equal(expected, parsed.Argument);
    }

    [Fact]
    public void WhenBothEndsAreVerbsTheFirstWins()
    {
        var parsed = GoBarParser.Parse("TF GP");

        Assert.Equal(GoBarVerb.Timeframe, parsed.Verb);
        Assert.Equal("GP", parsed.Argument);
        Assert.Equal(GoBarParseKind.Invalid, parsed.Kind);
    }

    [Theory]
    [InlineData("TF", GoBarVerb.Timeframe)]
    [InlineData("SYM", GoBarVerb.Symbol)]
    [InlineData("GP", GoBarVerb.Symbol)]
    [InlineData("PROV", GoBarVerb.Provider)]
    [InlineData("IND", GoBarVerb.Indicator)]
    [InlineData("ALERT", GoBarVerb.PriceAlert)]
    [InlineData("GOTO", GoBarVerb.GoTo)]
    [InlineData("FIND", GoBarVerb.Find)]
    public void AVerbWithNoValueIsTheBareVerb(string typed, GoBarVerb verb)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.BareVerb, parsed.Kind);
        Assert.Equal(verb, parsed.Verb);
    }

    // ── Bare shapes ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4h", "4h", "4 hours")]
    [InlineData("15m", "15m", "15 minutes")]
    [InlineData("1d", "1d", "1 day")]
    [InlineData("1w", "1w", "1 week")]
    [InlineData("1M", "1M", "1 month")]
    [InlineData("4 hours", "4h", "4 hours")]
    [InlineData("4hours", "4h", "4 hours")]
    [InlineData("15 min", "15m", "15 minutes")]
    [InlineData("15minutes", "15m", "15 minutes")]
    [InlineData("2 hour", "2h", "2 hours")]
    [InlineData("3hr", "3h", "3 hours")]
    [InlineData("2 days", "2d", "2 days")]
    [InlineData("2 wks", "2w", "2 weeks")]
    [InlineData("6 mo", "6M", "6 months")]
    public void ATimeframeIsABareShape(
        string typed, string expectedTimeframe, string expectedWords)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.BareTimeframe, parsed.Kind);
        Assert.Equal(expectedTimeframe, parsed.Timeframe);
        Assert.Equal(expectedWords, parsed.TimeframeWords);
    }

    [Fact]
    public void OneMinuteAndOneMonthAreDifferent()
    {
        var minutes = GoBarParser.Parse("1m");
        var months = GoBarParser.Parse("1M");

        Assert.Equal("1m", minutes.Timeframe);
        Assert.Equal("1 minute", minutes.TimeframeWords);
        Assert.Equal("1M", months.Timeframe);
        Assert.Equal("1 month", months.TimeframeWords);
    }

    [Fact]
    public void ATimeframeIsCappedAt999LikeTheToolbar()
    {
        var parsed = GoBarParser.Parse("1000h");

        Assert.Equal("999h", parsed.Timeframe);
        Assert.Equal("999 hours", parsed.TimeframeWords);
    }

    [Theory]
    [InlineData("2024-03-01", 2024, 3, 1, false)]
    [InlineData("2024-03-01 14:00", 2024, 3, 1, true)]
    public void AnIsoDateIsABareShape(string typed, int year, int month, int day, bool hasTime)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.BareDate, parsed.Kind);
        Assert.Equal(hasTime, parsed.MomentHasTime);
        Assert.Equal(new DateTime(year, month, day), parsed.Moment!.Value.Date);
    }

    // ── Prices ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("95000", 95000)]
    [InlineData("95,000", 95000)]
    [InlineData("95000.5", 95000.5)]
    [InlineData("95k", 95000)]
    [InlineData("1.2m", 1_200_000)]
    public void PricesParseInTheFormsPeopleType(string typed, double expected)
    {
        var parsed = GoBarParser.Parse($"ALERT {typed}");

        Assert.Equal(GoBarParseKind.Argument, parsed.Kind);
        Assert.Equal(expected, parsed.Price!.Value, 6);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-5")]
    [InlineData("-95k")]
    public void ZeroAndNegativePricesAreRefused(string typed)
    {
        var parsed = GoBarParser.Parse($"ALERT {typed}");

        Assert.Equal(GoBarParseKind.Invalid, parsed.Kind);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Refusal));
    }

    // ── Dates ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2024-03-01")]
    [InlineData("1 March 2024")]
    [InlineData("March 1 2024")]
    [InlineData("1 Mar 2024")]
    [InlineData("Mar 1, 2024")]
    public void DatesParseInTheFormsPeopleType(string typed)
    {
        var parsed = GoBarParser.Parse($"GOTO {typed}");

        Assert.Equal(GoBarParseKind.Argument, parsed.Kind);
        Assert.Equal(GoBarVerb.GoTo, parsed.Verb);
        Assert.NotNull(parsed.Moment);
        Assert.Equal(new DateTime(2024, 3, 1), parsed.Moment!.Value.Date);
    }

    [Theory]
    [InlineData("3/1/2024")]
    [InlineData("03/01/2024")]
    [InlineData("3/1/2024 14:00")]
    public void SlashDatesAreRefusedInWords(string typed)
    {
        var parsed = GoBarParser.Parse($"GOTO {typed}");

        Assert.Equal(GoBarParseKind.Invalid, parsed.Kind);
        Assert.Equal("Write the date as 2024-03-01 or 1 March 2024.", parsed.Refusal);
    }

    // ── Culture and money safety ───────────────────────────────────────────

    [Fact]
    public void TurkishDottedIDoesNotBreakAVerbOrABareShape()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            var timeframe = GoBarParser.Parse("4H TF");
            Assert.Equal(GoBarVerb.Timeframe, timeframe.Verb);
            Assert.Equal("4h", timeframe.Timeframe);

            var symbol = GoBarParser.Parse("btc gp");
            Assert.Equal(GoBarVerb.Symbol, symbol.Verb);
            Assert.Equal("btc", symbol.Argument);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("QSTOP 5")]
    [InlineData("QLIMIT 100")]
    [InlineData("QMARKET now")]
    [InlineData("QARMED 1")]
    public void AMoneyCodeFollowedByTextIsNotAnArgumentCommand(string typed)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.NotEqual(GoBarParseKind.Argument, parsed.Kind);
        Assert.Null(parsed.Verb);
    }
}

/// <summary>
/// Stage 6c's typed money reader. The verb is the first word, spelled exactly, and the two
/// values are strict positive decimals: a typo, a comma, an exponent, zero or a negative never
/// becomes a ticket.
/// </summary>
public class GoBarMoneyParserTests
{
    [Theory]
    [InlineData("BUY", GoBarVerb.Buy)]
    [InlineData("buy", GoBarVerb.Buy)]
    [InlineData("SeLl", GoBarVerb.Sell)]
    public void ASideAloneIsAMoneyCommand(string typed, GoBarVerb verb)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.Money, parsed.Kind);
        Assert.Equal(verb, parsed.Verb);
        Assert.Null(parsed.Quantity);
        Assert.Null(parsed.LimitPrice);
        Assert.False(parsed.Flat);
    }

    [Fact]
    public void AQuantityIsMarketAndAPriceIsLimit()
    {
        var market = GoBarParser.Parse("BUY 0.5");
        Assert.Equal(GoBarParseKind.Money, market.Kind);
        Assert.Equal(0.5, market.Quantity);
        Assert.Null(market.LimitPrice);
        Assert.True(market.Market);

        var limit = GoBarParser.Parse("BUY 0.5 95000");
        Assert.Equal(GoBarParseKind.Money, limit.Kind);
        Assert.Equal(0.5, limit.Quantity);
        Assert.Equal(95000, limit.LimitPrice);
        Assert.False(limit.Market);

        var withAt = GoBarParser.Parse("BUY 0.5 at 95000");
        Assert.Equal(GoBarParseKind.Money, withAt.Kind);
        Assert.Equal(0.5, withAt.Quantity);
        Assert.Equal(95000, withAt.LimitPrice);
        Assert.False(withAt.Market);
    }

    [Fact]
    public void FlatTakesNothing()
    {
        var parsed = GoBarParser.Parse("FLAT");

        Assert.Equal(GoBarParseKind.Money, parsed.Kind);
        Assert.Equal(GoBarVerb.Flat, parsed.Verb);
        Assert.True(parsed.Flat);
    }

    [Theory]
    [InlineData("BUY 1,5")]
    [InlineData("BUY 1e3")]
    [InlineData("BUY -1")]
    [InlineData("BUY 0")]
    [InlineData("BUY abc")]
    [InlineData("BUY half")]
    [InlineData("BUY 0.5 nope")]
    [InlineData("BUY 0.5 0")]
    public void ABadQuantityOrPriceIsRefusedInWords(string typed)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.Invalid, parsed.Kind);
        Assert.Equal(
            "BUY takes a quantity and an optional limit price, like BUY 0.5 or BUY 0.5 95000. "
            + "Nothing was opened.",
            parsed.Refusal);
    }

    [Fact]
    public void SellUsesItsOwnWordInTheRefusal()
    {
        var parsed = GoBarParser.Parse("SELL 0");

        Assert.Equal(GoBarParseKind.Invalid, parsed.Kind);
        Assert.Equal(
            "SELL takes a quantity and an optional limit price, like SELL 0.5 or SELL 0.5 95000. "
            + "Nothing was opened.",
            parsed.Refusal);
    }

    [Fact]
    public void FlatWithAnythingAfterItIsRefused()
    {
        var parsed = GoBarParser.Parse("FLAT BTCUSDT");

        Assert.Equal(GoBarParseKind.Invalid, parsed.Kind);
        Assert.Equal(
            "FLAT takes nothing; it uses the chart's symbol. Nothing was opened.",
            parsed.Refusal);
    }

    [Theory]
    [InlineData("0.5 BUY")]
    [InlineData("95000 SELL")]
    public void AMoneyVerbLastIsTheDeliberateRefusal(string typed)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.Invalid, parsed.Kind);
        Assert.Equal(
            "Money commands start with the word, like BUY 0.5. Nothing was opened.",
            parsed.Refusal);
    }

    [Theory]
    [InlineData("BYU 1")]
    [InlineData("BU")]
    [InlineData("SEL 2")]
    [InlineData("FLA")]
    [InlineData("BUYY")]
    [InlineData("FLATT")]
    public void ANearMissOrPrefixIsNotAMoneyVerb(string typed)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.NotEqual(GoBarParseKind.Money, parsed.Kind);
        Assert.NotEqual(GoBarVerb.Buy, parsed.Verb);
        Assert.NotEqual(GoBarVerb.Sell, parsed.Verb);
        Assert.NotEqual(GoBarVerb.Flat, parsed.Verb);
    }
}
