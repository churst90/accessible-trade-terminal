using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A support/resistance level is a claim about when it became knowable, and about what ends it.</b>
///
/// <para>
/// Cipher SR draws Market Cipher SR's levels (CipherSrMarketCipherFidelityTests): the high (low) of
/// a bar the two bars before climbed to in steps and the two bars after fell from in steps, taken
/// three bars after it — the first bar after its second right-hand bar closed — and held until a
/// newer level replaces it.
/// </para>
///
/// <para>
/// The A2h mutants that survived the old suite map onto this design: taking the level at the pivot
/// bar rather than the bar that can know it (look-ahead), and letting an equal high count as beaten.
/// The 2026-10-23 rule adds a third: a plain two-bar pivot (beats the two bars each side) instead of
/// a stepped one, which takes twice the levels Market Cipher SR takes.
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
    /// <b>A level is taken three bars after its pivot, from bars that exist by then, and only from a
    /// stepped pivot.</b> Every new resistance at bar c is the high of bar p = c - 3, with
    /// h[p-2] &lt; h[p-1] &lt; h[p] &gt; h[p+1] &gt; h[p+2] — bar c itself is not read. Likewise for
    /// support. Mutants that take the level at c - 1 or c - 2 (reading bars not yet closed), that let
    /// an equal high count, or that accept a plain two-bar pivot all fail here; the twin assertion
    /// checks every stepped pivot IS taken, so a provider that took nothing would fail too.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ALevelIsTakenOnTheBarThatCanFirstKnowIt(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 600);
        var r = Run(bars);

        foreach (var (line, isRes) in new[] { (CipherSrProvider.CompResistanceLine, true), (CipherSrProvider.CompSupportLine, false) })
        {
            double X(int i) => isRes ? bars[i].High : -bars[i].Low;
            bool Stepped(int p) => p >= 2 && p + 2 < bars.Count
                && X(p - 2) < X(p - 1) && X(p - 1) < X(p) && X(p) > X(p + 1) && X(p + 1) > X(p + 2);
            var z = Series(r, line);
            int taken = 0;
            var wrong = new List<string>();
            for (int c = 1; c < z.Length; c++)
            {
                if (double.IsNaN(z[c]) || z[c] == z[c - 1]) continue;
                taken++;
                int p = c - 3;
                if (p < 0 || !Stepped(p) || z[c] != (isRes ? bars[p].High : bars[p].Low))
                    wrong.Add($"{line} took {z[c]:F2} at bar {c}; bar {c - 3} is {(p >= 0 && Stepped(p) ? "a stepped pivot at another price" : "not a stepped pivot")}");
            }
            var missed = Enumerable.Range(2, bars.Count - 5)
                .Where(p => Stepped(p) && z[p + 3] != (isRes ? bars[p].High : bars[p].Low)).ToList();
            Assert.True(taken >= 5, $"only {taken} levels taken on probe series {flavour}");
            Assert.True(wrong.Count == 0, $"{wrong.Count} levels not taken from a stepped pivot three bars before:\n  " + string.Join("\n  ", wrong.Take(6)));
            Assert.True(missed.Count == 0, $"{missed.Count} stepped pivots not on the {line} three bars later (first at bar {missed.FirstOrDefault()})");
        }
    }

    /// <summary>
    /// <b>An equal step is not a step.</b> Market Cipher SR's highs must climb STRICTLY into the pivot
    /// and fall STRICTLY after it: letting an equal high count changed 22 to 99 bars of its levels on
    /// the 15-minute history it was read from. Here the only peak has one flat step — before it
    /// (<paramref name="flatAt"/> = 58/59) or after it (61/62) — and is not taken; with that step made
    /// strict, the same peak is (the vacuity twin).
    /// </summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    [InlineData(1, true)]
    public void AnEqualStepIsNotAClimb(int side, bool strict)
    {
        var bars = Bars(i =>
        {
            double hi = i <= 60 ? 100 + i * 0.25 : 115 - (i - 60) * 0.25;   // one peak, at bar 60
            if (side < 0 && i == 58) hi = 100 + 59 * 0.25 - (strict ? 0.1 : 0.0);   // flat into the peak
            if (side > 0 && i == 62) hi = 115 - 0.25 - (strict ? 0.1 : 0.0);        // flat out of it
            if (i == 60) hi = 115;
            return (hi, hi - 1.0, hi - 0.5);
        }, 120);

        var line = Series(Run(bars), CipherSrProvider.CompResistanceLine);
        if (strict)
            Assert.True(line[63] == 115, $"the strictly stepped peak at bar 60 was not taken at bar 63 (line[63] = {line[63]})");
        else
            Assert.True(double.IsNaN(line[63]) || line[63] != 115, $"a peak with a flat step on its {(side < 0 ? "left" : "right")} was taken as resistance");
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
        Assert.Equal(Peak, line[63]);
        Assert.True(bars[159].Close > Peak * 1.2, "the fixture never closes far beyond the level");
        Assert.True(line[159] == Peak,
            $"the {Peak} resistance was not carried after closes beyond it (line[159] = {line[159]}) — " +
            "a level ends only when a newer one replaces it.");
    }

    /// <summary>The vacuity twin of the rule above: a single clear peak IS taken, three bars after it and not before.</summary>
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
        Assert.True(line[63] == TopHigh, $"a single clear peak at bar 60 was not taken as resistance at bar 63 (line[63] = {line[63]})");
        for (int k = 60; k <= 62; k++)
            Assert.True(double.IsNaN(line[k]) || line[k] != TopHigh, $"the peak was on the line at bar {k} — before its second right-hand bar closed");
    }
}
