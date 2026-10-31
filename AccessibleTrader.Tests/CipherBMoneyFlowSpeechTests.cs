using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Money Flow says the number drawn, with its sign, on the pane's own scale — never a percentage,
/// because the value is not one (Cody, 2026-10-17: "Money flow wording, fix this"), and no side
/// words (Cody, 2026-10-30: "why does money flow say "x buying pressure" or "y selling pressure"
/// yes wt gives just a raw number read out? ... whatever scale thee two are set to should be what
/// is read out"). The Wave Gap says its number the same way.
/// </summary>
public sealed class CipherBMoneyFlowSpeechTests
{
    private static string? Say(string comp, double v) =>
        new CipherBProvider().GetComponentSpeech(comp, v,
            new Ohlcv(DateTime.UtcNow, 1, 1, 1, 1, 1), new Dictionary<string, double[]>(), 0);

    [Theory]
    [InlineData(17.97, "18.0")]
    [InlineData(-12.34, "-12.3")]
    [InlineData(0.0, "0.0")]
    public void Money_flow_says_the_signed_number_drawn(double v, string expected)
    {
        string? said = Say(CipherBProvider.CompMoneyFlowWave, v);
        Assert.Equal(expected, said);
        Assert.DoesNotContain("%", said);
        Assert.DoesNotContain("pressure", said);
    }

    [Theory]
    [InlineData(5.46, "5.5")]
    [InlineData(-3.04, "-3.0")]
    public void The_wave_gap_says_its_number_the_same_way(double v, string expected)
        => Assert.Equal(expected, Say(CipherBProvider.CompWtHistogram, v));
}
