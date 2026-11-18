using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests;

/// <summary>
/// The live notice sentences. Every one is pinned word for word, because a countdown is short
/// enough that a changed word is heard as a different fact.
/// </summary>
public class MarketEventNoticeWordingTests
{
    private static readonly DateTime T = new(2026, 3, 11, 12, 30, 0, DateTimeKind.Utc);

    private static MarketEvent Cpi() => MarketEventTestData.Event(
        id: "cpi-2026-03-11", spoken: "CPI", longName: "Consumer Price Index", moment: T)
        with { Source = "FRED release 10, fetched 2026-11-18" };

    private static MarketEventPriceSample Sample(double seconds, double close, string symbol = "BTC")
        => new(T.AddSeconds(seconds), symbol, close);

    [Fact]
    public void TheCountdownUsesTheSpokenNameAndTheSingularMinute()
    {
        Assert.Equal("CPI in 15 minutes.",
            MarketEventNoticeWording.Countdown(Cpi(), 15, Array.Empty<MarketEventHeldPosition>()));
        Assert.Equal("CPI in 1 minute.",
            MarketEventNoticeWording.Countdown(Cpi(), 1, Array.Empty<MarketEventHeldPosition>()));
    }

    [Fact]
    public void APositionRidesTheCountdownWithItsStop()
    {
        var positions = new[]
        {
            new MarketEventHeldPosition("BTC", 0.05, "MEXC", 89_000, MarketType.Crypto),
        };

        Assert.Equal(
            "CPI in 15 minutes. You're long 0.05 BTC on MEXC, stop at 89000.00.",
            MarketEventNoticeWording.Countdown(Cpi(), 15, positions));
    }

    [Fact]
    public void WithoutAWorkingStopTheStopIsLeftOut()
    {
        var positions = new[]
        {
            new MarketEventHeldPosition("ETH", 2, "Kraken", null, MarketType.Crypto),
        };

        Assert.Equal(
            "CPI in 15 minutes. You're long 2 ETH on Kraken.",
            MarketEventNoticeWording.Countdown(Cpi(), 15, positions));
    }

    [Fact]
    public void PaperIsSaidAsPaperAndAShortIsShort()
    {
        var positions = new[]
        {
            new MarketEventHeldPosition("SOL", -1.5, "Paper", null, MarketType.Crypto),
        };

        Assert.Equal(
            "CPI in 1 minute. You're short 1.5 SOL on Paper.",
            MarketEventNoticeWording.Countdown(Cpi(), 1, positions));
    }

    [Fact]
    public void AtMostThreePositionsAreNamedThenAndTwoMore()
    {
        var positions = Enumerable.Range(1, 5)
            .Select(i => new MarketEventHeldPosition($"S{i}", i, "MEXC", null, MarketType.Crypto))
            .ToArray();

        string text = MarketEventNoticeWording.Countdown(Cpi(), 15, positions);

        Assert.Contains("You're long 1 S1 on MEXC.", text);
        Assert.Contains("You're long 2 S2 on MEXC.", text);
        Assert.Contains("You're long 3 S3 on MEXC.", text);
        Assert.DoesNotContain("S4", text);
        Assert.EndsWith("And 2 more.", text);
    }

    [Fact]
    public void TheReadOutPutsThePriceReactionFirst()
    {
        var samples = new[] { Sample(0, 100), Sample(60, 99.2) };

        Assert.Equal("CPI out. BTC down 0.8% in the first minute.",
            MarketEventNoticeWording.ReadOut(Cpi(), samples, awaitingFigure: false));
    }

    [Theory]
    [InlineData(100, 100.4, "BTC up 0.4% in the first minute.")]
    [InlineData(100, 100.04, "BTC unchanged in the first minute.")]
    public void UpAndUnchangedAreSaidInWords(double start, double end, string expected)
    {
        var samples = new[] { Sample(0, start), Sample(60, end) };

        Assert.Equal("CPI out. " + expected,
            MarketEventNoticeWording.ReadOut(Cpi(), samples, awaitingFigure: false));
    }

    [Fact]
    public void AQuietFeedSaysOnlyThatTheEventIsOut()
    {
        // The focused chart's only updates land AFTER the first minute. There was no live
        // update between T and T + 60 s, so there is no reaction to report — the sabotage that
        // drops this check is red here.
        var samples = new[] { Sample(61, 100), Sample(65, 99) };

        Assert.Equal("CPI out.",
            MarketEventNoticeWording.ReadOut(Cpi(), samples, awaitingFigure: false));
    }

    [Fact]
    public void ASampleThatLandsTooLateForTheEventTimeIsNoReaction()
    {
        // The first update is at T + 10 s: the app was asleep at the release moment, so there
        // is no T price to measure from.
        var samples = new[] { Sample(10, 100), Sample(30, 98) };

        Assert.Equal("CPI out.",
            MarketEventNoticeWording.ReadOut(Cpi(), samples, awaitingFigure: false));
    }

    [Fact]
    public void AReleaseWithAKeyEndsWaitingForTheFigure()
    {
        var samples = new[] { Sample(0, 100), Sample(60, 99.2) };

        Assert.Equal(
            "CPI out. BTC down 0.8% in the first minute. Waiting for the figure.",
            MarketEventNoticeWording.ReadOut(Cpi(), samples, awaitingFigure: true));
    }

    [Fact]
    public void AFigureAlreadyHereUsesTheExistingWording()
    {
        var eventWithFigure = Cpi() with
        {
            Figures = new MarketEventFigures(new[]
            {
                new MarketEventFigure("CPI", 3.1, 2.9,
                    MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
            }),
        };
        var samples = new[] { Sample(0, 100), Sample(60, 99.2) };

        string expected = "CPI out. BTC down 0.8% in the first minute. "
            + MarketEventSpeech.FigureClause(eventWithFigure);
        Assert.Equal(expected,
            MarketEventNoticeWording.ReadOut(eventWithFigure, samples, awaitingFigure: false));
        Assert.Equal(MarketEventSpeech.FigureClause(eventWithFigure),
            MarketEventNoticeWording.Figure(eventWithFigure));
    }
}
