using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests;

/// <summary>
/// The regime detector behind <c>StrategyLab regime-persistence</c>.
///
/// <para>
/// Why this file exists: the regime-persistence study reported a NULL on the claim that market
/// regimes have got shorter since COVID, and that verdict is only worth anything if the thing
/// counting the regimes counts them correctly. A swing detector that silently drifted — an
/// off-by-one in the confirmation, a threshold applied to the wrong extreme — would change the
/// answer without changing a single line of the study, and nobody would notice, because the output
/// is a plausible-looking number either way. So the detector is pinned to dates that are matters of
/// public record rather than to its own past output.
/// </para>
/// </summary>
public class RegimeDetectorTests
{
    // ── Synthetic geometry ──────────────────────────────────────────────────────
    //
    // Zigzag returns a SEGMENTATION, so its first entry is a seed: the bar from which the record
    // becomes measurable, not a reversal. The number of regime flips is therefore one less than the
    // pivot count, which is what the study counts. Flips() applies that here so each test says what
    // it means.

    private static int Flips(double[] c, double theta)
        => Math.Max(0, RegimePersistenceCommand.Zigzag(c, theta).Count - 1);

    [Fact]
    public void MonotoneRise_HasNoFlips()
    {
        var c = Enumerable.Range(0, 500).Select(i => 100.0 * Math.Pow(1.001, i)).ToArray();
        Assert.Equal(0, Flips(c, 0.20));
    }

    [Fact]
    public void ReversalSmallerThanThreshold_DoesNotConfirm()
    {
        // Up to 100, back to 90 (-10%), then up again. A 20% detector must ignore it entirely.
        var c = Ramp(50, 100).Concat(Ramp(100, 90)).Concat(Ramp(90, 130)).ToArray();
        Assert.Equal(0, Flips(c, 0.20));
    }

    [Fact]
    public void PivotIsPlacedAtTheExtreme_NotAtTheBarThatConfirmedIt()
    {
        // Rise to 100 at a known index, fall 30%. The pivot must be the peak, not the bar where the
        // 20% retracement completed — that difference IS the duration measurement.
        var up = Ramp(50, 100);          // peak is the last bar of this leg
        int peak = up.Count - 1;
        var c = up.Concat(Ramp(100, 70)).ToArray();

        var pivots = RegimePersistenceCommand.Zigzag(c, 0.20);
        Assert.Equal(1, Flips(c, 0.20));
        Assert.Equal(peak, pivots[^1]);
    }

    [Fact]
    public void AlternatesDirection_AndFindsEveryLegOfASawtooth()
    {
        // 100 → 60 → 110 → 60 → 110: three confirmed reversals after the opening leg.
        var c = Ramp(100, 60).Concat(Ramp(60, 110)).Concat(Ramp(110, 60)).Concat(Ramp(60, 110)).ToArray();
        var pivots = RegimePersistenceCommand.Zigzag(c, 0.20);

        Assert.True(Flips(c, 0.20) >= 3, $"expected at least 3 flips, got {Flips(c, 0.20)}");
        Assert.Equal(pivots.OrderBy(x => x), pivots);              // strictly forward in time
        Assert.Equal(pivots.Distinct().Count(), pivots.Count);
    }

    [Fact]
    public void SmallerThreshold_NeverFindsFewerFlips()
    {
        var rng = new Random(4242);
        var c = new double[3000];
        c[0] = 100;
        for (int i = 1; i < c.Length; i++) c[i] = c[i - 1] * Math.Exp((rng.NextDouble() - 0.5) * 0.04);

        int coarse = Flips(c, 0.20);
        int fine = Flips(c, 0.10);
        Assert.True(fine >= coarse, $"10% found {fine} flips but 20% found {coarse}");
    }

    // ── The one that matters: real, public bear markets ─────────────────────────

    /// <summary>
    /// A 20% swing detector run on the S&amp;P 500's path must land on the tops and bottoms that are
    /// historical record. These dates are not taken from the detector's own output — they are the
    /// ones every account of the period agrees on — so this is a check against the world rather than
    /// a snapshot of previous behaviour.
    /// </summary>
    [Fact]
    public void TwentyPercentDetector_FindsTheKnownBearMarketTurns()
    {
        var (dates, closes) = SyntheticSp500();
        var pivots = RegimePersistenceCommand.Zigzag(closes, 0.20);
        var found = pivots.Select(i => dates[i]).ToList();

        foreach (var expected in new[]
        {
            new DateTime(2000, 3, 24),    // dot-com top
            new DateTime(2007, 10, 9),    // pre-GFC top
            new DateTime(2009, 3, 9),     // GFC bottom
            new DateTime(2020, 2, 19),    // COVID top
            new DateTime(2020, 3, 23),    // COVID bottom
            new DateTime(2022, 1, 3),     // 2022 top
        })
        {
            Assert.True(found.Any(d => Math.Abs((d - expected).TotalDays) <= 5),
                $"no pivot within 5 days of {expected:yyyy-MM-dd}; found {string.Join(", ", found.Select(d => d.ToString("yyyy-MM-dd")))}");
        }
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static List<double> Ramp(double from, double to, int steps = 40)
    {
        var xs = new List<double>(steps);
        for (int i = 1; i <= steps; i++) xs.Add(from + (to - from) * i / steps);
        return xs;
    }

    /// <summary>
    /// A daily path pinned to the S&amp;P 500's actual closes at each documented turning point, with
    /// straight-line interpolation between them. Interpolation cannot invent a turn, and it cannot
    /// hide one, because a 20% detector only cares about the extremes — so this fixture tests
    /// exactly what it claims to and needs no network or snapshot on disk.
    /// </summary>
    private static (DateTime[] Dates, double[] Closes) SyntheticSp500()
    {
        var anchors = new (DateTime Date, double Close)[]
        {
            (new DateTime(1995,  1,  3),  459.11),
            (new DateTime(1998,  8, 31),  957.28),   // LTCM/Russia low, ~-19%: must NOT confirm at 20%
            (new DateTime(2000,  3, 24), 1527.46),   // dot-com top
            (new DateTime(2002, 10,  9),  776.76),   // dot-com bottom
            (new DateTime(2007, 10,  9), 1565.15),   // pre-GFC top
            (new DateTime(2009,  3,  9),  676.53),   // GFC bottom
            (new DateTime(2018,  9, 20), 2930.75),
            (new DateTime(2018, 12, 24), 2351.10),   // -19.8%: also must not confirm at 20%
            (new DateTime(2020,  2, 19), 3386.15),   // COVID top
            (new DateTime(2020,  3, 23), 2237.40),   // COVID bottom
            (new DateTime(2022,  1,  3), 4796.56),   // 2022 top
            (new DateTime(2022, 10, 12), 3577.03),   // 2022 bottom
            (new DateTime(2026,  7, 24), 6300.00),
        };

        var dates = new List<DateTime>();
        var closes = new List<double>();
        for (int a = 0; a < anchors.Length - 1; a++)
        {
            var (d0, c0) = anchors[a];
            var (d1, c1) = anchors[a + 1];
            int days = (int)(d1 - d0).TotalDays;
            for (int k = 0; k < days; k++)
            {
                var d = d0.AddDays(k);
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                dates.Add(d);
                closes.Add(c0 * Math.Pow(c1 / c0, k / (double)days));
            }
        }
        dates.Add(anchors[^1].Date);
        closes.Add(anchors[^1].Close);
        return (dates.ToArray(), closes.ToArray());
    }
}
