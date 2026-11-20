using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Fed decision's rate from Kalshi (KXFED, FED before 2026): what was priced before a
/// meeting, the rate the settled ladder decided, the one before it, and the words for all three.
/// The feed itself, against saved responses, is in <see cref="MarketEventKalshiFeedTests"/>.
/// </summary>
public class MarketEventFedRateTests
{
    private static readonly DateOnly October = new(2026, 10, 28);
    private static readonly DateOnly December = new(2026, 12, 9);

    private static MarketEvent Meeting(DateOnly date)
        => MarketEventSeed.Load().Events.Single(e => e.Id == $"fomc-{date:yyyy-MM-dd}");

    private static JObject Market(double strike, string result, string type = "greater")
        => new() { ["strike_type"] = type, ["floor_strike"] = strike, ["result"] = result };

    private static IEnumerable<JObject> Settled(double rate, params double[] strikes)
        => strikes.Select(s => Market(s, rate > s ? "yes" : "no"));

    // ── The pure rules ──

    [Fact]
    public void AMeetingNamesItsOwnMonth()
    {
        Assert.Equal(new[] { "KXFED-26DEC", "FED-26DEC" },
            KalshiExpectedFigures.EventTickers(KalshiExpectedFigures.FedRateName, December));
        // A release still names the month before it.
        Assert.Equal("KXCPI-26NOV", KalshiExpectedFigures.EventTickers("CPI", December)[0]);
    }

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(0.1, 1)]
    [InlineData(0.25, 2)]
    public void TheStepKeepsItsOwnDecimals(double step, int decimals)
        => Assert.Equal(decimals, KalshiExpectedFigures.Decimals(step));

    [Fact]
    public void AQuarterPointLadderGivesAQuarterPointRate()
    {
        // December 2026's open ladder, 18 November: 91% above 4.00, 2.5% above 4.25.
        var quotes = new[]
        {
            new KalshiStrikeProbability(3.75, 0.99),
            new KalshiStrikeProbability(4.00, 0.91),
            new KalshiStrikeProbability(4.25, 0.025),
            new KalshiStrikeProbability(4.50, 0.005),
        };

        Assert.Equal(4.25, KalshiExpectedFigures.Median(quotes, 0.25));
    }

    [Fact]
    public void ASettledLadderGivesTheDecidedRate()
    {
        var strikes = new[] { 3.50, 3.75, 4.00, 4.25, 4.50 };

        Assert.Equal(4.00, KalshiExpectedFigures.SettledValue(Settled(4.00, strikes), 0.25));
        Assert.Equal(4.25, KalshiExpectedFigures.SettledValue(Settled(4.25, strikes).Reverse(), 0.25));
        // Markets of another shape are not rungs.
        Assert.Equal(4.00, KalshiExpectedFigures.SettledValue(
            Settled(4.00, strikes).Append(Market(9, "yes", type: "between")), 0.25));
    }

    public static IEnumerable<object[]> UnsettledLadders()
    {
        yield return new object[] { "one market still open",
            new[] { Market(3.75, "yes"), Market(4.00, ""), Market(4.25, "no") } };
        // Skipping the open rung would leave a clean 3.75 yes, 4.00 no and say 4.00 too soon.
        yield return new object[] { "the top market still open",
            new[] { Market(3.75, "yes"), Market(4.00, "no"), Market(4.25, "") } };
        yield return new object[] { "a gap wider than a step",
            new[] { Market(3.75, "yes"), Market(4.25, "no") } };
        yield return new object[] { "a yes above a no",
            new[] { Market(3.75, "yes"), Market(4.00, "no"), Market(4.25, "yes") } };
        yield return new object[] { "every rung yes", new[] { Market(3.75, "yes"), Market(4.00, "yes") } };
        yield return new object[] { "every rung no", new[] { Market(3.75, "no"), Market(4.00, "no") } };
    }

    [Theory]
    [MemberData(nameof(UnsettledLadders))]
    public void ALadderThatDoesNotSayOneRateGivesNone(string why, JObject[] markets)
        => Assert.True(KalshiExpectedFigures.SettledValue(markets, 0.25) is null, why);

    // ── The calendar ──

    private sealed class FakeExpected : IMarketEventExpectedFeed
    {
        public double? Expected;
        public double? Actual;
        public double? Previous;
        public int Version => 1;

        public double? ExpectedFor(string source, DateOnly releaseDate, string figureName)
            => figureName == KalshiExpectedFigures.FedRateName || source != "fomc" ? Expected : null;

        public void RequestRefresh(WorkspaceState state) { }

        public (double? Actual, double? Previous) SettledFor(string source, DateOnly releaseDate, string figureName)
            => figureName == KalshiExpectedFigures.FedRateName ? (Actual, Previous) : (null, null);
    }

    [Fact]
    public void AFedDecisionGainsItsRateAndAReleaseNever()
    {
        var feed = new FakeExpected { Expected = 4.25, Actual = 4.25, Previous = 4.00 };
        var calendar = new MarketEventCalendar(expectedFeed: feed);

        var fed = calendar.ApplyExpected(Meeting(December));
        var rate = Assert.Single(fed.Figures!.Figures);
        Assert.Equal(new MarketEventFigure(KalshiExpectedFigures.FedRateName, 4.25, 4.00,
            MarketEventFigureUnit.Rate, MarketEventFigureDirection.HigherIsHotter, 4.25), rate);

        var cpi = MarketEventSeed.CreateRelease("cpi", December, null, "2026-11-20");
        Assert.DoesNotContain(calendar.ApplyExpected(cpi).Figures!.Figures,
            f => f.Name == KalshiExpectedFigures.FedRateName);
    }

    [Fact]
    public void AnUpcomingMeetingCarriesThePricedRateAndTheRateNow()
    {
        var calendar = new MarketEventCalendar(expectedFeed: new FakeExpected { Expected = 4.25, Previous = 4.00 });

        var fed = calendar.ApplyExpected(Meeting(December));

        Assert.Equal("expected 4.25%", MarketEventSpeech.ExpectedShort(fed));
        Assert.Equal("rate now 4.00%", MarketEventSpeech.PreviousFigureClause(fed));
        Assert.Equal("", MarketEventSpeech.FigureClause(fed));
        Assert.Equal("Figure: Fed rate unavailable, expected 4.25%, by Kalshi. Previous: 4.00%. "
            + "Rate as Kalshi settled it from the Federal Reserve's published target.",
            MarketEventSpeech.FigureDetail(fed));
    }

    [Fact]
    public void TheCountdownSaysThePricedRate()
    {
        var fed = new MarketEventCalendar(expectedFeed: new FakeExpected { Expected = 4.25, Previous = 4.00 })
            .ApplyExpected(Meeting(December));

        Assert.Equal("Fed decision, expected 4.25%, in 15 minutes.",
            MarketEventNoticeWording.Countdown(fed, 15, Array.Empty<MarketEventHeldPosition>()));
    }

    [Fact]
    public void AMeetingWithNothingReadStaysAsItWas()
    {
        var meeting = Meeting(October);
        Assert.Same(meeting, new MarketEventCalendar(expectedFeed: new FakeExpected()).ApplyExpected(meeting));
    }

    // ── The words ──

    private static MarketEvent Decided(double rate, double? previous, double? expected)
        => new MarketEventCalendar(expectedFeed: new FakeExpected
        {
            Actual = rate, Previous = previous, Expected = expected,
        }).ApplyExpected(Meeting(October));

    [Theory]
    [InlineData(4.00, 4.00, "held at 4.00%")]
    [InlineData(4.25, 4.00, "raised to 4.25% from 4.00%")]
    [InlineData(3.75, 4.00, "cut to 3.75% from 4.00%")]
    [InlineData(4.25, null, "rate 4.25%")]
    public void TheDecisionIsSaidAsAMove(double rate, double? previous, string said)
        => Assert.Equal(said, MarketEventSpeech.FigureClause(Decided(rate, previous, null)));

    [Theory]
    [InlineData(4.00, 4.00, "as expected")]
    [InlineData(4.25, 4.00, "higher than expected")]
    [InlineData(3.75, 4.00, "lower than expected")]
    public void TheSurpriseIsHigherOrLower(double rate, double expected, string word)
        => Assert.Equal(word, MarketEventSpeech.PrimarySurprise(Decided(rate, 4.00, expected)));

    [Fact]
    public void TheBarSaysTheMoveThenTheSurprise()
    {
        var fed = Decided(3.75, 4.00, 4.00);

        string said = MarketEventSpeech.Clause(
            new[] { new MarketEventBarPlacement(fed, 0, null) },
            1800, MarketEventLevel.All, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.EndsWith(", cut to 3.75% from 4.00%, lower than expected", said);
        Assert.StartsWith("Fed decision", said);
    }

    [Fact]
    public void TheDetailSaysTheRateThePriceAndTheSource()
        => Assert.Equal(
            "Figure: Fed rate, upper bound of the target range, raised to 4.25% from 4.00%, "
            + "expected 4.00%, by Kalshi, higher than expected. Previous: 4.00%. "
            + "Rate as Kalshi settled it from the Federal Reserve's published target.",
            MarketEventSpeech.FigureDetail(Decided(4.25, 4.00, 4.00)));
}
