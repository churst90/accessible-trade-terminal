using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A support/resistance level is a claim about when it became knowable, and about what ends it.</b>
///
/// <para>
/// Cipher SR draws Market Cipher SR's levels (CipherSrMarketCipherFidelityTests): the high (low) of
/// a bar that beat the <c>LeftBars</c> bars before it and was not beaten by the bar after it, taken
/// on that next bar — the first bar that can know — and held until a newer level replaces it.
/// </para>
///
/// <para>
/// The A2h mutants that survived the old suite map onto this design: taking the level at the pivot
/// bar rather than the bar that can know it (look-ahead), and letting an equal high count as beaten.
/// Engineered series are used where a precise shape is the point — a rise that closes far beyond a
/// level without forming a new one is in the probe series only by accident.
/// </para>
/// </summary>
public sealed class CipherSrZoneRulesTests
{
    private static readonly CipherSrProvider Provider = new();

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars,
        params (string Key, object Value)[] overrides)
    {
        // Volume filter off: the shapes under test are about price, and a constant volume never
        // clears its own average.
        var pars = new Dictionary<string, object> { ["VolumeMA"] = 0.0 };
        foreach (var (k, v) in overrides) pars[k] = v;

        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        Provider.Calculate("CIPHER_SR",
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan((List<Ohlcv>)bars),
            pars, buffer);
        return results;
    }

    private static double[] Series(Dictionary<string, double[]> r, string component)
        => r.TryGetValue(component, out var a) ? a : Array.Empty<double>();

    private static List<Ohlcv> Bars(Func<int, (double High, double Low, double Close)> shape, int n)
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            var (h, l, c) = shape(i);
            bars.Add(new Ohlcv(t.AddHours(i), c, h, l, c, 1000));
        }
        return bars;
    }

    // ── The confirmation lag ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>A level is taken on the bar after its pivot, from bars that exist by then.</b> Every new
    /// resistance at bar c is the high of bar c - 1, STRICTLY higher than the <c>LeftBars</c> highs
    /// before it and than bar c's own high — the right-hand bar is c itself, so nothing after c is
    /// read. Likewise for support. A mutant that takes the pivot at c (which needs bar c + 1) puts
    /// bar c's own high on the line, and one that lets an equal high count as beaten takes levels
    /// this rejects; both fail here.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ALevelIsTakenOnTheBarThatCanFirstKnowIt(int flavour)
    {
        const int Left = 3;
        var bars = CausalityProbeSeries.Bars(flavour, 600);
        var r = Run(bars, ("LeftBars", Left));

        foreach (var (line, isRes) in new[] { (CipherSrProvider.CompResistanceLine, true), (CipherSrProvider.CompSupportLine, false) })
        {
            var z = Series(r, line);
            int taken = 0;
            var wrong = new List<string>();
            for (int c = 1; c < z.Length; c++)
            {
                if (double.IsNaN(z[c]) || z[c] == z[c - 1]) continue;
                taken++;
                int p = c - 1;
                double v = isRes ? bars[p].High : bars[p].Low;
                bool ok = z[c] == v
                    && (isRes ? bars[c].High < v : bars[c].Low > v)
                    && Enumerable.Range(1, Left).All(k => p - k >= 0 && (isRes ? bars[p - k].High < v : bars[p - k].Low > v));
                if (!ok) wrong.Add($"{line} took {z[c]:F2} at bar {c}; bar {p}'s {(isRes ? "high" : "low")} is {v:F2}");
            }
            Assert.True(taken > 10, $"only {taken} levels taken on probe series {flavour}");
            Assert.True(wrong.Count == 0, $"{wrong.Count} levels not taken from the bar before:\n  " + string.Join("\n  ", wrong.Take(6)));
        }
    }

    // ── What ends a level ───────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Closing beyond a level does not end it; only a newer level does.</b> A clean peak, then a
    /// strictly rising stretch that closes far above it: every high is beaten by the next, so no new
    /// pivot forms and the peak's level must still be carried. (Market Cipher SR held a 36,614
    /// resistance for 17 hours with price 4% above it.)
    /// </summary>
    [Fact]
    public void ACloseBeyondALevelDoesNotEndIt()
    {
        const double Peak = 125.0;
        var bars = Bars(i =>
        {
            if (i < 60) return (100 + i * 0.2, 99 + i * 0.2, 99.5 + i * 0.2);
            if (i == 60) return (Peak, 118.0, 120.0);                    // the pivot high
            if (i == 61) return (119.0, 116.0, 117.0);                   // fails to beat it
            double c = 117.0 + (i - 61) * 0.5;                           // strictly rising, past 125
            return (c + 0.4, c - 0.4, c);
        }, 160);

        var line = Series(Run(bars), CipherSrProvider.CompResistanceLine);
        Assert.Equal(Peak, line[61]);
        Assert.True(bars[159].Close > Peak * 1.2, "the fixture never closes far beyond the level");
        Assert.True(line[159] == Peak,
            $"the {Peak} resistance was not carried after closes beyond it (line[159] = {line[159]}) — " +
            "a level ends only when a newer one replaces it.");
    }

    /// <summary>The vacuity twin of the rule above: a single clear peak IS taken, on the next bar.</summary>
    [Fact]
    public void ASingleClearPeakIsALevel()
    {
        const double TopHigh = 130.0;
        var bars = Bars(i =>
        {
            if (i == 60) return (TopHigh, 120.0, 124.0);
            double c = i < 60 ? 100 + i * 0.35 : 121.0 - (i - 60) * 0.35;
            return (c + 1.0, c - 1.0, c);
        }, 200);

        var line = Series(Run(bars), CipherSrProvider.CompResistanceLine);
        Assert.True(line[61] == TopHigh, $"a single clear peak at bar 60 was not taken as resistance at bar 61 (line[61] = {line[61]})");
        Assert.True(double.IsNaN(line[60]) || line[60] != TopHigh, "the peak was on the line at its own bar — before anything could know it");
    }
}
