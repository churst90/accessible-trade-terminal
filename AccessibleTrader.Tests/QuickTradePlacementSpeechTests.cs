using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// What a user hears when they commit a quick trade.
///
/// <para>
/// <b>The defect, from live paper trading.</b> Pressing Ctrl+Enter, the last thing spoken was the
/// single word "sent" — no fill, no price, no quantity. The cause was ordering.
/// <c>Place()</c> published the order first; the executor ran synchronously into the paper broker,
/// which fills a market order immediately and announces "Order filled…" with <c>interrupt: true</c>;
/// control then returned and the "…Sent." line interrupted <i>that</i>. The intent talked over the
/// outcome, and the outcome is the part that carries what actually happened.
/// </para>
///
/// <para>
/// The same change fixes the chattiness. The placement line used to repeat the entire calculator —
/// side, quantity, entry, stop, cash at risk — every word of which had been spoken one keypress
/// earlier when the stop was set, and all of which the broker's fill announcement then said again.
/// One action, three recitals of the same trade.
/// </para>
/// </summary>
public class QuickTradePlacementSpeechTests
{
    private static (QuickTradeService Svc, SpyEventBus Bus) Armed(double equity = 100_000)
    {
        var bars = Enumerable.Range(0, 50).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i),
            Open = 100, High = 105, Low = 95, Close = 100, Volume = 1
        }).ToArray();

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 25,
            Identity = new ChartIdentity("Crypto", "MEXC", "BTCUSDT", "4h"),
        });

        var bus = new SpyEventBus();
        var svc = new QuickTradeService(store, bus, () => equity);
        svc.Arm(1.0);
        svc.SetStopAtCursor();
        return (svc, bus);
    }

    /// <summary>
    /// The exact reported case. A broker that fills synchronously must get the last word, so the
    /// intent has to be spoken before the order is published.
    /// </summary>
    [Fact]
    public void ThePlacementIsSpokenBeforeTheOrderIsPublished()
    {
        var (svc, bus) = Armed();
        int before = bus.Log.Count;

        svc.Place(market: true);

        var after = bus.Log.Skip(before).ToList();
        int speechAt = after.FindIndex(e => e is FeedbackRequestEvent f && f.Message?.Contains("sent", StringComparison.OrdinalIgnoreCase) == true);
        int orderAt  = after.FindIndex(e => e is QuickTradeRequestedEvent);

        Assert.True(speechAt >= 0, "The placement must be announced.");
        Assert.True(orderAt >= 0, "The order must be published.");
        Assert.True(speechAt < orderAt,
            "The order was published before the confirmation was spoken, so a broker that fills "
          + "synchronously announces the fill and this line then interrupts it — which is how "
          + "\"sent\" became the last thing a user heard after committing real size.");
    }

    /// <summary>
    /// One keypress, one short sentence. The numbers live in the stop announcement before it and the
    /// fill announcement after it.
    /// </summary>
    [Fact]
    public void ThePlacementLineDoesNotRepeatTheWholeCalculator()
    {
        var (svc, bus) = Armed();
        int before = bus.Log.Count;

        svc.Place(market: true);

        string spoken = string.Join(" ", bus.Log.Skip(before).OfType<FeedbackRequestEvent>().Select(e => e.Message));

        Assert.Contains("sent", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("risking", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stop", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, "market")]
    [InlineData(false, "limit")]
    public void ItStillSaysWhichKindOfOrderWent(bool market, string expected)
    {
        var (svc, bus) = Armed();
        int before = bus.Log.Count;

        svc.Place(market);

        string spoken = string.Join(" ", bus.Log.Skip(before).OfType<FeedbackRequestEvent>().Select(e => e.Message));
        Assert.Contains(expected, spoken, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Sizes must never be read out as scientific notation.
///
/// <para>
/// <b>The defect.</b> The order book formatted sizes with <c>ToString("G4")</c>. The <c>G</c>
/// specifier switches to exponent form once the exponent reaches the precision, so a Kaspa book
/// holding 74,200,000 units rendered as <c>7.42E+07</c> — which a screen reader pronounces as
/// "seven point four two E plus zero seven". Prices had the mirror-image problem: <c>G6</c> turns
/// 0.0000123 into <c>1.23E-05</c>.
/// </para>
/// </summary>
public class QuantityFormatterTests
{
    /// <summary>The reported case.</summary>
    [Fact]
    public void ALargeCryptoSizeIsNeverScientificNotation()
    {
        string s = QuantityFormatter.Format(74_200_000);
        Assert.DoesNotContain("E", s, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("74,200,000", s);
    }

    [Theory]
    [InlineData(1e12)]
    [InlineData(74_200_000)]
    [InlineData(12_345.678)]
    [InlineData(1)]
    [InlineData(0.0034)]
    [InlineData(0.000000123)]
    [InlineData(-98_765_432)]
    public void NoMagnitudeProducesAnExponent(double v)
    {
        Assert.DoesNotContain("E", QuantityFormatter.Format(v), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("E", QuantityFormatter.FormatCompact(v), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Decimals only where they carry information.</summary>
    [Theory]
    [InlineData(74_200_000, "74,200,000")]
    [InlineData(12.5, "12.50")]
    [InlineData(0.034, "0.0340")]
    public void PrecisionFollowsMagnitude(double v, string expected)
        => Assert.Equal(expected, QuantityFormatter.Format(v));

    /// <summary>A small size must not be rounded away to zero.</summary>
    [Fact]
    public void ASmallSizeKeepsEnoughFiguresToBeDistinguishable()
    {
        Assert.NotEqual(QuantityFormatter.Format(0.0034), QuantityFormatter.Format(0.0035));
        Assert.NotEqual("0", QuantityFormatter.Format(0.000000123));
    }

    [Theory]
    [InlineData(74_200_000, "74.2M")]
    [InlineData(2_500_000_000, "2.5B")]
    public void CompactAbbreviatesOnlyAboveAMillion(double v, string expected)
        => Assert.Equal(expected, QuantityFormatter.FormatCompact(v));

    /// <summary>Abbreviating five figures costs precision and saves nothing.</summary>
    [Fact]
    public void CompactLeavesThousandsAlone()
        => Assert.Equal("12,400", QuantityFormatter.FormatCompact(12_400));

    /// <summary>Screen readers pronounce a bare "M" unpredictably, so speech gets the word.</summary>
    [Fact]
    public void SpokenFormUsesWordsNotSuffixes()
    {
        Assert.Contains("million", QuantityFormatter.FormatSpoken(74_200_000));
        Assert.Contains("billion", QuantityFormatter.FormatSpoken(2_500_000_000));
    }

    [Fact]
    public void NonFiniteValuesDoNotThrowOrLie()
    {
        Assert.Equal("—", QuantityFormatter.Format(double.NaN));
        Assert.Equal("unknown", QuantityFormatter.FormatSpoken(double.NaN));
    }
}
