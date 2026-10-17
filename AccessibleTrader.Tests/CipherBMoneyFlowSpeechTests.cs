using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Money Flow says which side it is on and the number drawn — never a percentage, because the value
/// is not one (Cody, 2026-10-17: "Money flow wording, fix this").
/// </summary>
public sealed class CipherBMoneyFlowSpeechTests
{
    private static string? Say(double v) =>
        new CipherBProvider().GetComponentSpeech(CipherBProvider.CompMoneyFlowWave, v,
            new Ohlcv(DateTime.UtcNow, 1, 1, 1, 1, 1), new Dictionary<string, double[]>(), 0);

    [Theory]
    [InlineData(17.97, "buying pressure, 18.0")]
    [InlineData(-12.34, "selling pressure, 12.3")]
    [InlineData(0.0, "neutral, 0.0")]
    public void It_names_the_side_and_the_drawn_number_without_a_percent(double v, string expected)
    {
        string? said = Say(v);
        Assert.Equal(expected, said);
        Assert.DoesNotContain("%", said);
    }
}
