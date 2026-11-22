using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Regime state while the 200-bar average warms up is no reading, not "flat": a 0 there
/// was the same number a bar exactly on its average gets, and a strategy gating on "not
/// bearish" passed through the warm-up.
/// </summary>
public sealed class RegimeProviderTests
{
    [Fact]
    public void The_state_is_NaN_until_the_average_fills_then_a_sign()
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var data = Enumerable.Range(0, 260)
            .Select(i => new Ohlcv(t.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000))
            .ToArray();
        var results = new Dictionary<string, double[]>();
        new RegimeProvider().Calculate(RegimeProvider.Code, data, new Dictionary<string, object>(),
            new IndicatorResultBuffer(results, data.Length));
        var state = results[RegimeProvider.CompRegimeState];

        Assert.All(state.Take(199), v => Assert.True(double.IsNaN(v)));
        // A steady rise closes above its own average from the first full window on.
        Assert.All(state.Skip(199), v => Assert.Equal(1.0, v));
    }
}
