using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the Cosasverdes "origin line" claim: that an asset's swing highs and lows fall on a
/// family of PARALLEL, EQUIDISTANT lines in log-price space — one slope, one spacing, copies
/// stacked above and below a master line anchored at the origin of price.
///
/// <para>
/// THE STATISTIC. Naively counting touches is useless, because halving the spacing doubles the
/// number of lines and therefore the touches. Instead each pivot is mapped to a PHASE: where it
/// sits between two adjacent family lines, as a fraction in [0,1). If the family is real, pivots
/// cluster near phase 0; if it is imaginary, phases are uniform. Non-uniformity is measured with
/// the Rayleigh resultant R = |mean(e^(2πi·phase))| — 0 for uniform, 1 for perfect clustering.
/// R is scale-free, so wide and narrow spacings compete fairly.
/// </para>
///
/// <para>
/// THE CONTROL. R is maximised over a grid of (slope, spacing), and maximising anything over a
/// grid inflates it. The surrogate runs therefore perform the IDENTICAL grid search on
/// block-bootstrapped price series and take their own maximum. Comparing max-to-max is what
/// makes the p-value honest; comparing a fitted real value against an unfitted null would
/// manufacture significance out of nothing.
/// </para>
/// </summary>
public static class OriginLineCommand
{
    private const int SlopeSteps = 61;
    private const int SpacingSteps = 60;
    private const int PivotSpan = 8;

    public sealed record Fit(double SlopePerBar, double SpacingLog, double R, int Pivots);

    public static Task<int> RunAsync(string snapshotDir, string? only, string tf, int surrogates)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        if (files.Count == 0) { Console.Error.WriteLine($"No {tf} snapshots matched."); return Task.FromResult(1); }

        Console.WriteLine($"===== ORIGIN LINES: parallel equidistant log-price families ({tf}) =====");
        Console.WriteLine("R = Rayleigh clustering of pivots between adjacent family lines (0 = none).");
        Console.WriteLine($"p = fraction of {surrogates} surrogates whose OWN best fit matched or beat the real one.");
        Console.WriteLine();
        Console.WriteLine($"  {"asset",-16} {"pivots",6} {"R",6} {"surrR",6} {"spacing",9} {"doublings",9} {"p",6}");

        var rows = new List<(string Asset, Fit Real, double SurrMean, double P)>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 500) continue;

            var real = BestFit(bars);
            if (real.Pivots < 30) continue;

            var rng = new Random(StableSeed.From(snap.Symbol) % 100000);
            var surrR = new List<double>();
            int beaten = 0;
            for (int s = 0; s < surrogates; s++)
            {
                var surrogate = SurrogateTest.BlockBootstrap(bars, rng);
                var fit = BestFit(surrogate);
                if (fit.Pivots < 10) continue;
                surrR.Add(fit.R);
                if (fit.R >= real.R) beaten++;
            }

            double mean = surrR.Count > 0 ? surrR.Average() : double.NaN;
            double p = (beaten + 1.0) / (surrR.Count + 1.0);
            rows.Add(($"{snap.Symbol} {tf}", real, mean, p));

            // Spacing expressed as price doublings, which is how a log grid is actually read.
            double doublings = real.SpacingLog / Math.Log10(2);
            Console.WriteLine($"  {snap.Symbol + " " + tf,-16} {real.Pivots,6} {real.R,6:0.000} {mean,6:0.000} " +
                              $"{real.SpacingLog,9:0.0000} {doublings,9:0.00} {p,6:0.000}");
        }

        if (rows.Count > 0)
        {
            int sig = rows.Count(r => r.P <= 0.05);
            Console.WriteLine();
            Console.WriteLine($"  VERDICT: {sig}/{rows.Count} assets showed grid clustering beyond their own surrogates");
            Console.WriteLine($"  at p<=0.05 (expected by chance: ~{rows.Count * 0.05:0.0}).");
            Console.WriteLine($"  Mean real R {rows.Average(r => r.Real.R):0.000} vs mean surrogate R {rows.Average(r => r.SurrMean):0.000}.");
        }

        return Task.FromResult(0);
    }

    /// <summary>
    /// The decisive test. Fits the family on the first <paramref name="fraction"/> of history,
    /// then measures clustering on the REMAINDER with those parameters frozen. A grid that is
    /// genuine structure keeps working on bars it was never fitted to; a grid that is
    /// curve-fitting collapses to the surrogate level. Surrogates run the identical
    /// fit-then-freeze procedure so the comparison stays fair.
    /// </summary>
    public static Task<int> RunHoldoutAsync(string snapshotDir, string? only, string tf,
        int surrogates, double fraction)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        Console.WriteLine($"===== ORIGIN LINES, OUT OF SAMPLE ({tf}, fit on first {fraction:P0}) =====");
        Console.WriteLine("inR = clustering on the fitted segment. oosR = same family, later pivots, no refit.");
        Console.WriteLine($"p = fraction of {surrogates} surrogates whose OWN out-of-sample score matched or beat it.");
        Console.WriteLine();
        Console.WriteLine($"  {"asset",-16} {"inN",5} {"oosN",5} {"inR",6} {"oosR",6} {"surr",6} {"p",6}");

        var rows = new List<(string Asset, double OosR, double SurrMean, double P)>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 500) continue;

            var (inPiv, outPiv) = SplitPivots(bars, fraction);
            if (inPiv.Count < 25 || outPiv.Count < 15) continue;

            var fit = BestFit(inPiv);
            double oos = ScoreFixed(outPiv, fit.SlopePerBar, fit.SpacingLog);

            var rng = new Random(StableSeed.From(snap.Symbol) % 100000);
            var surr = new List<double>();
            int beaten = 0;
            for (int s = 0; s < surrogates; s++)
            {
                var sb = SurrogateTest.BlockBootstrap(bars, rng);
                var (si, so) = SplitPivots(sb, fraction);
                if (si.Count < 10 || so.Count < 5) continue;
                var sf = BestFit(si);
                double sv = ScoreFixed(so, sf.SlopePerBar, sf.SpacingLog);
                if (double.IsNaN(sv)) continue;
                surr.Add(sv);
                if (sv >= oos) beaten++;
            }

            double mean = surr.Count > 0 ? surr.Average() : double.NaN;
            double p = (beaten + 1.0) / (surr.Count + 1.0);
            rows.Add(($"{snap.Symbol} {tf}", oos, mean, p));
            Console.WriteLine($"  {snap.Symbol + " " + tf,-16} {inPiv.Count,5} {outPiv.Count,5} " +
                              $"{fit.R,6:0.000} {oos,6:0.000} {mean,6:0.000} {p,6:0.000}");
        }

        if (rows.Count > 0)
        {
            int sig = rows.Count(r => r.P <= 0.05);
            Console.WriteLine();
            Console.WriteLine($"  VERDICT: {sig}/{rows.Count} assets held up out of sample at p<=0.05 " +
                              $"(expected ~{rows.Count * 0.05:0.0}).");
            Console.WriteLine($"  Mean oosR {rows.Average(r => r.OosR):0.000} vs surrogate {rows.Average(r => r.SurrMean):0.000}.");
        }
        return Task.FromResult(0);
    }

    /// <summary>Grid-searches (slope, spacing) for the family with the tightest pivot clustering.</summary>
    public static Fit BestFit(IReadOnlyList<Ohlcv> bars) => BestFit(Pivots(bars));

    /// <summary>
    /// Rayleigh clustering of a pivot set against a FIXED family. This is what makes the
    /// out-of-sample test possible: fit (slope, spacing) on early history, then score later
    /// pivots without re-optimising anything.
    /// </summary>
    public static double ScoreFixed(List<(double T, double LogP)> pivots, double slope, double spacing)
    {
        if (pivots.Count == 0 || spacing <= 0) return double.NaN;
        double sumCos = 0, sumSin = 0;
        foreach (var (t, logP) in pivots)
        {
            double phase = (logP - slope * t) / spacing;
            double angle = 2 * Math.PI * (phase - Math.Floor(phase));
            sumCos += Math.Cos(angle);
            sumSin += Math.Sin(angle);
        }
        return Math.Sqrt(sumCos * sumCos + sumSin * sumSin) / pivots.Count;
    }

    /// <summary>Splits pivots at a bar index — in-sample below, out-of-sample at or above.</summary>
    public static (List<(double T, double LogP)> In, List<(double T, double LogP)> Out)
        SplitPivots(IReadOnlyList<Ohlcv> bars, double fraction)
    {
        var all = Pivots(bars);
        double cut = bars.Count * fraction;
        return (all.Where(p => p.T < cut).ToList(), all.Where(p => p.T >= cut).ToList());
    }

    public static Fit BestFit(List<(double T, double LogP)> pivots)
    {
        if (pivots.Count < 10) return new Fit(0, 0, 0, pivots.Count);

        // Search slopes around the least-squares log-price trend — the family's master line has
        // to be in that neighbourhood or it would not track the asset at all.
        double lsSlope = LeastSquaresSlope(pivots);
        double slopeSpan = Math.Abs(lsSlope) * 2 + 1e-4;

        // Spacing search range. The upper bound MUST be data-dependent: if the spacing approaches
        // the asset's whole detrended log range, every pivot falls in a single cell and the phase
        // statistic saturates near 1 for trivial reasons. (First run: 12 of 13 assets pinned at
        // the fixed upper bound and SPY/QQQ posted R≈0.93 — that was the degeneracy, not signal.)
        // Requiring at least MinLines cells keeps the family a grid rather than a single band.
        const int MinLines = 5;
        double lo = pivots.Min(p => p.LogP - lsSlope * p.T);
        double hi = pivots.Max(p => p.LogP - lsSlope * p.T);
        double detrendedRange = Math.Max(hi - lo, 1e-6);

        double minSpacing = Math.Log10(2) / 8;
        double maxSpacing = Math.Max(minSpacing * 2, detrendedRange / MinLines);

        double bestR = -1, bestSlope = 0, bestSpacing = 0;

        for (int si = 0; si < SlopeSteps; si++)
        {
            double slope = lsSlope - slopeSpan + 2 * slopeSpan * si / (SlopeSteps - 1.0);

            for (int gi = 0; gi < SpacingSteps; gi++)
            {
                double spacing = minSpacing * Math.Pow(maxSpacing / minSpacing, gi / (SpacingSteps - 1.0));

                double sumCos = 0, sumSin = 0;
                foreach (var (t, logP) in pivots)
                {
                    double detrended = logP - slope * t;
                    double phase = detrended / spacing;
                    double angle = 2 * Math.PI * (phase - Math.Floor(phase));
                    sumCos += Math.Cos(angle);
                    sumSin += Math.Sin(angle);
                }

                double r = Math.Sqrt(sumCos * sumCos + sumSin * sumSin) / pivots.Count;
                if (r > bestR) { bestR = r; bestSlope = slope; bestSpacing = spacing; }
            }
        }

        return new Fit(bestSlope, bestSpacing, bestR, pivots.Count);
    }

    /// <summary>Confirmed swing highs and lows as (barIndex, log10 price) pairs.</summary>
    private static List<(double T, double LogP)> Pivots(IReadOnlyList<Ohlcv> bars)
    {
        var result = new List<(double, double)>();
        for (int i = PivotSpan; i < bars.Count - PivotSpan; i++)
        {
            bool isHigh = true, isLow = true;
            for (int j = i - PivotSpan; j <= i + PivotSpan && (isHigh || isLow); j++)
            {
                if (j == i) continue;
                if (bars[j].High >= bars[i].High) isHigh = false;
                if (bars[j].Low <= bars[i].Low) isLow = false;
            }
            if (isHigh && bars[i].High > 0) result.Add((i, Math.Log10(bars[i].High)));
            if (isLow && bars[i].Low > 0) result.Add((i, Math.Log10(bars[i].Low)));
        }
        return result;
    }

    private static double LeastSquaresSlope(List<(double T, double LogP)> pts)
    {
        double n = pts.Count;
        double sx = pts.Sum(p => p.T), sy = pts.Sum(p => p.LogP);
        double sxx = pts.Sum(p => p.T * p.T), sxy = pts.Sum(p => p.T * p.LogP);
        double denom = n * sxx - sx * sx;
        return Math.Abs(denom) < 1e-12 ? 0 : (n * sxy - sx * sy) / denom;
    }
}
