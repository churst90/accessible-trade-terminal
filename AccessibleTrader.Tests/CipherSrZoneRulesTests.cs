using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A support/resistance zone is a claim about when a level became knowable, and about what
/// destroys it.</b>
///
/// <para>
/// All three A2h mutants aimed at <see cref="CipherSrProvider"/> survived a green suite: the zone
/// line could step to a level at the pivot bar rather than at the bar the pivot became provable
/// (a look-ahead that the causality contract did not catch), a resistance level could be broken by
/// a close BELOW it, and a pivot high could tolerate an equal high beside it.
/// </para>
///
/// <para>
/// <b>Why the look-ahead one matters more than it looks.</b> A pivot at bar <c>i</c> needs bars
/// <c>i+1..i+pb</c> to be a pivot at all. Confirming the zone at <c>i</c> draws it up to fifteen
/// bars before it exists, and a strategy comparing price to that level reads a number derived from
/// bars it has not seen. That was fixed on 2026-08-21 and, until now, nothing held it fixed.
/// </para>
///
/// <para>
/// Two of these use engineered series rather than the shared probe generator. A double top with
/// two exactly-equal highs, and a level that is approached but never closed through, are precise
/// shapes — the probe series contain them only by accident, if at all, and a test that depends on
/// an accident is a test that will start failing for the wrong reason.
/// </para>
/// </summary>
public sealed class CipherSrZoneRulesTests
{
    private static readonly CipherSrProvider Provider = new();

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars,
        params (string Key, object Value)[] overrides)
    {
        // AutoScale off: with it on the pivot window is derived from the bar's own index, which is
        // right for a chart and makes "the zone appears pb bars after the pivot" a moving target.
        var pars = new Dictionary<string, object> { ["AutoScale"] = 0, ["VolumeMultiplier"] = 0.0 };
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
    /// <b>The zone steps to a level at the bar the pivot became provable, not at the pivot.</b>
    ///
    /// <para>
    /// Asserted against the provider's own two components: the dot marks the pivot bar (and is
    /// declared Lookahead for exactly that reason), the zone line is declared Causal. The level's
    /// first appearance in the zone must be at least <c>PivotBars</c> bars after the dot.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AZoneNeverAppearsBeforeItsPivotIsProvable(int flavour)
    {
        const int PivotBars = 5;
        var bars = CausalityProbeSeries.Bars(flavour, 600);
        var r = Run(bars, ("PivotBars", PivotBars));

        foreach (var (dotComponent, lineComponent) in new[]
                 {
                     (CipherSrProvider.CompResistance, CipherSrProvider.CompResistanceLine),
                     (CipherSrProvider.CompSupport, CipherSrProvider.CompSupportLine),
                 })
        {
            var dots = Series(r, dotComponent);
            var line = Series(r, lineComponent);

            int checkedCount = 0;
            var early = new List<string>();

            for (int p = 0; p < dots.Length; p++)
            {
                if (double.IsNaN(dots[p])) continue;
                double level = dots[p];
                checkedCount++;

                // The first bar at which this level is carried by the zone.
                for (int i = p; i < line.Length; i++)
                {
                    if (double.IsNaN(line[i]) || line[i] != level) continue;
                    if (i <= p + PivotBars)
                        early.Add($"{lineComponent} carried {level:F2} at bar {i}, only {i - p} bars " +
                                  $"after its pivot at {p}");
                    break;
                }
            }

            Assert.True(checkedCount > 0, $"no {dotComponent} pivots on series {flavour}");
            Assert.True(early.Count == 0,
                $"{early.Count} zone levels appeared before their pivot was provable on series " +
                $"{flavour}:\n  " + string.Join("\n  ", early.Take(6)) +
                $"\nA pivot at bar i needs bars i+1..i+{PivotBars} to be a pivot at all.");
        }
    }

    // ── What breaks a level ─────────────────────────────────────────────────────────

    /// <summary>
    /// <b>A resistance level is broken by a close convincingly ABOVE it — and "convincingly" is the
    /// whole of the threshold.</b>
    ///
    /// <para>
    /// The A2h mutant moved the threshold to the wrong side of the level, from
    /// <c>lastRes * (1 + breakPct)</c> to <c>lastRes * (1 - breakPct)</c>. That does not invert the
    /// comparison — it moves the break trigger slightly BELOW the level instead of slightly above
    /// it, so a bar that closes exactly at resistance, or a hair under it, destroys the zone.
    /// Reaching a level and failing there is the most ordinary thing resistance does, and it is
    /// precisely the bar on which this reports the level gone.
    /// </para>
    ///
    /// <para>
    /// So the discriminating case is a close INSIDE the threshold band: above
    /// <c>level × (1 − pct)</c> and below <c>level × (1 + pct)</c>. Engineered deliberately,
    /// because a band of 0.4% of the price is not a width a random series lands in on purpose.
    /// </para>
    /// </summary>
    [Fact]
    public void ACloseThatReachesResistanceWithoutClearingItDoesNotBreakTheZone()
    {
        const int PivotBars = 5;
        const double Peak = 125.0;
        const double BreakPercent = 0.2;                 // per cent, so the band is +/-0.25 of 125

        // A clean peak, then a STRICTLY DECLINING stretch — no local maximum, so no second pivot
        // can form and replace the level — and then one bar that closes just under the level,
        // inside the threshold band on both sides.
        //
        // The declining stretch is not decoration. A first version used a gentle sine and its
        // peaks became pivots of their own, so by the time the touch arrived the zone was carrying
        // a different level entirely and the test failed for a reason that had nothing to do with
        // the break rule.
        const double Touch = 124.9;                      // inside 125 x (1 +/- 0.002)
        var bars = Bars(i =>
        {
            if (i < 60) return (100 + i * 0.2, 99 + i * 0.2, 99.5 + i * 0.2);
            if (i == 60) return (Peak, 118.0, 120.0);                    // the pivot high
            if (i == 120) return (Touch + 0.05, 120.0, Touch);           // reaches resistance, fails
            double c = 116.0 - (i - 61) * 0.05;                          // strictly declining
            return (c + 0.5, c - 0.5, c);
        }, 300);

        var r = Run(bars, ("PivotBars", PivotBars), ("AdaptiveBreak", false), ("BreakThreshold", BreakPercent));
        var line = Series(r, CipherSrProvider.CompResistanceLine);

        Assert.True(Series(r, CipherSrProvider.CompResistance).Any(v => !double.IsNaN(v)),
            "the engineered peak produced no resistance pivot");
        Assert.True(!double.IsNaN(line[119]) && Math.Abs(line[119] - Peak) < 0.01,
            $"the {Peak} zone was not being carried on the bar before the touch (line[119] = " +
            $"{line[119]}) — the fixture never reaches the case.");

        // The bars immediately AFTER the touch, and before the touching bar's own pivot could be
        // confirmed PivotBars later. Checking a window further out would be satisfied by the level
        // being re-established by a new pivot, which is a different event.
        var dropped = new List<int>();
        for (int i = 121; i < 121 + PivotBars - 1; i++)
            if (double.IsNaN(line[i]) || Math.Abs(line[i] - Peak) >= 0.01) dropped.Add(i);

        Assert.True(dropped.Count == 0,
            $"a bar that closed at {Touch}, just under {Peak}, destroyed the {Peak} resistance zone — it was gone " +
            $"on bars {string.Join(", ", dropped)}. The break threshold sits ABOVE the level: price " +
            "reaching resistance and failing there is the level working, not the level breaking.");
    }

    /// <summary>
    /// The other direction, so "the zone survives" cannot be satisfied by a zone that never breaks:
    /// a close convincingly above the level must destroy it.
    /// </summary>
    [Fact]
    public void AResistanceZoneIsBrokenByACloseAboveIt()
    {
        const int PivotBars = 5;
        const double Peak = 125.0;

        var bars = Bars(i =>
        {
            if (i < 60) return (100 + i * 0.2, 99 + i * 0.2, 99.5 + i * 0.2);
            if (i == 60) return (Peak, 118.0, 120.0);
            if (i < 140) { double c = 112.0 + 3.0 * Math.Sin(i / 5.0); return (c + 1.5, c - 1.5, c); }
            double up = 130.0 + (i - 140) * 0.4;                          // decisively through
            return (up + 1.5, up - 1.5, up);
        }, 300);

        var r = Run(bars, ("PivotBars", PivotBars), ("AdaptiveBreak", false), ("BreakThreshold", 0.2));
        var line = Series(r, CipherSrProvider.CompResistanceLine);

        bool stillCarrying = false;
        for (int i = 250; i < line.Length; i++)
            if (!double.IsNaN(line[i]) && Math.Abs(line[i] - Peak) < 0.01) { stillCarrying = true; break; }

        Assert.False(stillCarrying,
            $"the {Peak} resistance zone was still being carried after price closed and held above " +
            "130 — a level price has closed through is no longer resistance.");
    }

    // ── What counts as a pivot ──────────────────────────────────────────────────────

    /// <summary>
    /// <b>A pivot high is STRICTLY the greatest in its window, so a double top is not two pivots.</b>
    ///
    /// <para>
    /// The A2h mutant relaxed <c>&lt;=</c> to <c>&lt;</c>, so an equal high beside the candidate no
    /// longer disqualifies it. A flat top then emits a resistance pivot on each of its bars — two
    /// dots, two spoken levels and two zone steps for one price structure.
    /// </para>
    ///
    /// <para>
    /// Engineered with two bars at exactly the same high, inside each other's window.
    /// </para>
    /// </summary>
    [Fact]
    public void ADoubleTopWithEqualHighsIsNotAPivot()
    {
        const int PivotBars = 5;
        const double TopHigh = 130.0;

        // A rise, then two bars sharing exactly the same high three bars apart (inside the
        // five-bar window), then a fall. Neither is strictly greatest, so neither is a pivot.
        var bars = Bars(i =>
        {
            if (i == 60 || i == 63) return (TopHigh, 120.0, 124.0);
            double c = i < 61 ? 100 + i * 0.35 : 121.0 - (i - 63) * 0.35;
            return (c + 1.0, c - 1.0, c);
        }, 200);

        var r = Run(bars, ("PivotBars", PivotBars));
        var dots = Series(r, CipherSrProvider.CompResistance);

        var atTheTop = new List<int>();
        for (int i = 55; i <= 70 && i < dots.Length; i++)
            if (!double.IsNaN(dots[i]) && Math.Abs(dots[i] - TopHigh) < 0.01) atTheTop.Add(i);

        Assert.True(atTheTop.Count == 0,
            $"a double top with two exactly-equal highs produced {atTheTop.Count} resistance pivots " +
            $"(bars {string.Join(", ", atTheTop)}). A pivot high must be STRICTLY the greatest in its " +
            "window, or one price structure becomes two levels.");
    }

    /// <summary>
    /// The vacuity twin: a single clear peak, with nothing equal beside it, MUST be a pivot. Without
    /// this, the double-top assertion is satisfied by a detector that finds no pivots at all.
    /// </summary>
    [Fact]
    public void ASingleClearPeakIsAPivot()
    {
        const int PivotBars = 5;
        const double TopHigh = 130.0;

        var bars = Bars(i =>
        {
            if (i == 60) return (TopHigh, 120.0, 124.0);
            double c = i < 60 ? 100 + i * 0.35 : 121.0 - (i - 60) * 0.35;
            return (c + 1.0, c - 1.0, c);
        }, 200);

        var dots = Series(Run(bars, ("PivotBars", PivotBars)), CipherSrProvider.CompResistance);

        Assert.True(!double.IsNaN(dots[60]) && Math.Abs(dots[60] - TopHigh) < 0.01,
            $"a single clear peak at bar 60 was not detected as a resistance pivot (dot value " +
            $"{dots[60]}).");
    }
}
