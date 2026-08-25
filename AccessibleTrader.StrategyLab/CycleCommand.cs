using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the Camel Finance / Bob Lucas / Charles Nana cycle system.
///
/// <para>
/// THE CLAIMS. Bitcoin runs a 60-day daily cycle measured low to low, ±10% (so 54–66 days); the
/// S&amp;P 500 runs 40 days (36–44). Lows land inside the timing band <b>80% of the time</b>.
/// Where the cycle HIGH falls relative to the midpoint is the directional tell — right-translated is
/// bullish, left-translated bearish. A "cycle failure" (price undercutting the prior cycle low)
/// means more downside at least into the next low.
/// </para>
///
/// <para>
/// THE TRAP THIS COMMAND EXISTS TO AVOID. Cycle lows in the tutorials are identified by looking
/// back at a finished chart. If you pick the lows knowing the outcome, they will be 54–66 days apart
/// BECAUSE YOU CHOSE THEM THAT WAY, and the 80% figure measures the selection, not the market. So
/// lows here are found by a fixed algorithm — a pivot low that is the lowest of the <c>span</c> bars
/// either side, and therefore only knowable <c>span</c> bars later, which is also the delay any real
/// trade would suffer.
/// </para>
///
/// <para>
/// THE CONTROL. Any pivot detector produces lows at a spacing set mostly by its own span parameter.
/// Every statistic is therefore recomputed on SURROGATE series built by shuffling the log returns
/// and rebuilding the price path — same return distribution, same volatility, all temporal structure
/// destroyed. If a shuffled random walk yields "60-day cycles" 80% of the time too, the cycle is a
/// property of the detector and not of Bitcoin.
/// </para>
///
/// <para>
/// Periodicity and translation are tested SEPARATELY and on purpose. "Did the high come early or
/// late in this swing" is a momentum statement that can carry information even if the fixed period
/// is an artefact — so the tradeable claim does not depend on the 60 days being real.
/// </para>
/// </summary>
public static class CycleCommand
{
    private const int Surrogates = 200;
    private const int HorizonBars = 20;

    private sealed record Cycle(int LowIdx, int NextLowIdx, int HighIdx, double Translation, bool Failed);

    public static int Run(string snapshotDir, string tf, int permutations)
    {
        // The two markets the system is actually claimed for, with their claimed bands.
        var targets = new (string Match, string Label, int Centre, int Lo, int Hi)[]
        {
            ("bitstamp_BTC_USDT", "BTC  (claimed 60d)", 60, 54, 66),
            ("SPY",               "SPY  (claimed 40d)", 40, 36, 44),
            ("QQQ",               "QQQ  (claimed 40d)", 40, 36, 44),
            ("bitstamp_ETH_USDT", "ETH  (not claimed)", 60, 54, 66),
        };

        Console.WriteLine();
        LabStats.ReportPermutationCap("cycle", permutations, PermutationCap);
        Console.WriteLine("===== CAMEL / BOB LUCAS / CHARLES NANA CYCLES =====");
        Console.WriteLine("Lows found algorithmically: a pivot low that is the lowest of `span` bars either side,");
        Console.WriteLine($"knowable only `span` bars later. Surrogates = {Surrogates} return-shuffled random walks.");
        Console.WriteLine();

        foreach (var t in targets)
        {
            var file = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
                .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(f => Path.GetFileName(f).Contains(t.Match, StringComparison.OrdinalIgnoreCase));
            if (file == null) { Console.WriteLine($"  {t.Label}: no snapshot"); continue; }

            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            if (snap.Bars.Count < 600) continue;

            Analyse(snap, t.Label, t.Centre, t.Lo, t.Hi, permutations);
        }

        return 0;
    }

    private static void Analyse(SnapshotFile snap, string label, int centre, int lo, int hi, int permutations)
    {
        var bars = snap.Bars;
        Console.WriteLine($"══════ {label} — {snap.Symbol}, {bars.Count:N0} bars, " +
                          $"{bars[0].Date:yyyy-MM} → {bars[^1].Date:yyyy-MM} ══════");

        // ── Periodicity, against the control ────────────────────────────────
        Console.WriteLine($"  {"span",5} {"lows",6} {"mean gap",9} {"median",7} {"in band",8}  │ {"SURROGATE mean",15} {"in band",8}");

        int bestSpan = 0; double bestDelta = double.MaxValue;
        foreach (int span in new[] { 5, 8, 10, 12, 15, 20, 25 })
        {
            var lows = PivotLows(bars, span);
            if (lows.Count < 10) continue;
            var gaps = Gaps(lows, bars);
            if (gaps.Count < 8) continue;

            double mean = gaps.Average();
            double inBand = gaps.Count(g => g >= lo && g <= hi) / (double)gaps.Count;

            var (sMean, sBand) = SurrogateStats(bars, span, lo, hi);

            Console.WriteLine($"  {span,5} {lows.Count,6} {mean,9:0.0} {Median(gaps),7:0.0} {inBand,8:P0}  │ " +
                              $"{sMean,15:0.0} {sBand,8:P0}");

            if (Math.Abs(mean - centre) < bestDelta) { bestDelta = Math.Abs(mean - centre); bestSpan = span; }
        }

        if (bestSpan == 0) { Console.WriteLine("  no usable span"); Console.WriteLine(); return; }

        Console.WriteLine($"  → span {bestSpan} lands closest to the claimed {centre}d. Read its SURROGATE columns:");
        Console.WriteLine("    if the shuffled random walk matches, the spacing is the detector, not the market.");
        Console.WriteLine();

        // ── Translation and failure, which do not need the period to be real ─
        var cycles = BuildCycles(bars, PivotLows(bars, bestSpan));
        if (cycles.Count < 20) { Console.WriteLine("  too few cycles for the directional tests"); Console.WriteLine(); return; }

        Translation(bars, cycles, bestSpan, permutations);
        Failure(bars, cycles, bestSpan, permutations);
        Console.WriteLine();
    }

    /// <summary>
    /// CLAIM: right-translated (high late in the cycle) is bullish, left-translated bearish.
    /// Measured on the forward return AFTER the cycle's end plus the detector's confirmation lag,
    /// so the translation is genuinely knowable before the return is earned.
    /// </summary>
    private static void Translation(IReadOnlyList<Ohlcv> bars, List<Cycle> cycles, int span, int permutations)
    {
        var rows = new List<(double Trans, double Fwd)>();
        foreach (var c in cycles)
        {
            int at = c.NextLowIdx + span;               // when the cycle is confirmed complete
            if (at + HorizonBars >= bars.Count) continue;
            if (bars[at].Close <= 0) continue;
            rows.Add((c.Translation, Math.Log(bars[at + HorizonBars].Close / bars[at].Close)));
        }
        if (rows.Count < 20) { Console.WriteLine("  translation: too few"); return; }

        var right = rows.Where(r => r.Trans > 0.5).ToList();
        var left = rows.Where(r => r.Trans <= 0.5).ToList();
        Console.WriteLine($"  translation: right {(right.Count >= 5 ? right.Average(r => r.Fwd).ToString("+0.00%;-0.00%;0") : "n/a"),8} (n={right.Count,3})   " +
                          $"left {(left.Count >= 5 ? left.Average(r => r.Fwd).ToString("+0.00%;-0.00%;0") : "n/a"),8} (n={left.Count,3})");

        if (right.Count < 8 || left.Count < 8) { Console.WriteLine("    (too lopsided to test)"); return; }
        double gap = right.Average(r => r.Fwd) - left.Average(r => r.Fwd);
        double p = PermutationP(rows.Select(r => r.Fwd).ToArray(), right.Count, left.Count, gap, permutations);
        Console.WriteLine($"    right − left: {gap:+0.00%;-0.00%;0}   p = {p:0.0000}" + (p <= 0.05 ? "  *" : "") +
                          (gap > 0 ? "   (sign matches the claim)" : "   (sign is BACKWARDS)"));
    }

    /// <summary>
    /// CLAIM: a cycle that undercuts the previous cycle low means more downside is coming.
    /// </summary>
    private static void Failure(IReadOnlyList<Ohlcv> bars, List<Cycle> cycles, int span, int permutations)
    {
        var rows = new List<(bool Failed, double Fwd)>();
        foreach (var c in cycles)
        {
            int at = c.NextLowIdx + span;
            if (at + HorizonBars >= bars.Count || bars[at].Close <= 0) continue;
            rows.Add((c.Failed, Math.Log(bars[at + HorizonBars].Close / bars[at].Close)));
        }
        var failed = rows.Where(r => r.Failed).ToList();
        var held = rows.Where(r => !r.Failed).ToList();
        if (failed.Count < 8 || held.Count < 8) { Console.WriteLine("  failure: too few"); return; }

        double gap = failed.Average(r => r.Fwd) - held.Average(r => r.Fwd);
        double p = PermutationP(rows.Select(r => r.Fwd).ToArray(), failed.Count, held.Count, gap, permutations);
        Console.WriteLine($"  cycle failure: failed {failed.Average(r => r.Fwd),8:+0.00%;-0.00%;0} (n={failed.Count,3})   " +
                          $"held {held.Average(r => r.Fwd),8:+0.00%;-0.00%;0} (n={held.Count,3})");
        Console.WriteLine($"    failed − held: {gap:+0.00%;-0.00%;0}   p = {p:0.0000}" + (p <= 0.05 ? "  *" : "") +
                          (gap < 0 ? "   (sign matches the claim)" : "   (sign is BACKWARDS)"));
    }

    // ── Detection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pivot lows: bar i is a low if its Low is the smallest in [i-span, i+span]. Only knowable at
    /// i+span, which is exactly the lag the tutorials' "wait for a swing low / trendline break /
    /// moving-average cross to confirm it" imposes in practice.
    /// </summary>
    private static List<int> PivotLows(IReadOnlyList<Ohlcv> bars, int span)
    {
        var lows = new List<int>();
        for (int i = span; i < bars.Count - span; i++)
        {
            double v = bars[i].Low;
            bool isLow = true;
            for (int j = i - span; j <= i + span && isLow; j++)
                if (j != i && bars[j].Low < v) isLow = false;
            if (isLow) lows.Add(i);
        }
        return lows;
    }

    private static List<double> Gaps(List<int> lows, IReadOnlyList<Ohlcv> bars)
    {
        var g = new List<double>();
        for (int i = 1; i < lows.Count; i++)
            g.Add((bars[lows[i]].Date - bars[lows[i - 1]].Date).TotalDays);
        return g;
    }

    private static List<Cycle> BuildCycles(IReadOnlyList<Ohlcv> bars, List<int> lows)
    {
        var cycles = new List<Cycle>();
        for (int i = 1; i < lows.Count; i++)
        {
            int a = lows[i - 1], b = lows[i];
            if (b - a < 4) continue;

            int highIdx = a;
            for (int j = a; j <= b; j++) if (bars[j].High > bars[highIdx].High) highIdx = j;

            double trans = (highIdx - a) / (double)(b - a);
            bool failed = i >= 2 && bars[b].Low < bars[lows[i - 2]].Low;
            cycles.Add(new Cycle(a, b, highIdx, trans, failed));
        }
        return cycles;
    }

    // ── The control ──────────────────────────────────────────────────────────

    /// <summary>
    /// Same detector on return-shuffled random walks. Shuffling destroys every trace of periodicity
    /// while preserving the return distribution and overall volatility, so whatever spacing survives
    /// is manufactured by the detector.
    /// </summary>
    private static (double MeanGap, double InBand) SurrogateStats(IReadOnlyList<Ohlcv> bars, int span, int lo, int hi)
    {
        var rets = new List<double>();
        for (int i = 1; i < bars.Count; i++)
            if (bars[i].Close > 0 && bars[i - 1].Close > 0) rets.Add(Math.Log(bars[i].Close / bars[i - 1].Close));
        if (rets.Count < 100) return (double.NaN, double.NaN);

        double meanAcc = 0, bandAcc = 0;
        int used = 0;
        var rng = new Random(12345 + span);
        var work = rets.ToArray();

        for (int s = 0; s < Surrogates; s++)
        {
            for (int i = work.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (work[i], work[j]) = (work[j], work[i]); }

            // Rebuild an OHLC path. The bar's own high/low range is carried across as a fraction of
            // its close so the detector sees a comparably shaped bar, not a pure close-only series.
            var synth = new Ohlcv[bars.Count];
            double px = bars[0].Close;
            synth[0] = bars[0];
            for (int i = 1; i < bars.Count; i++)
            {
                px *= Math.Exp(work[i - 1]);
                double refClose = bars[i].Close > 0 ? bars[i].Close : px;
                double hiF = bars[i].High / refClose, loF = bars[i].Low / refClose;
                synth[i] = new Ohlcv
                {
                    Date = bars[i].Date,
                    Open = px,
                    High = px * Math.Max(1, hiF),
                    Low = px * Math.Min(1, loF),
                    Close = px,
                    Volume = bars[i].Volume,
                };
            }

            var lows = PivotLows(synth, span);
            if (lows.Count < 10) continue;
            var gaps = Gaps(lows, synth);
            if (gaps.Count < 8) continue;

            meanAcc += gaps.Average();
            bandAcc += gaps.Count(g => g >= lo && g <= hi) / (double)gaps.Count;
            used++;
        }

        return used == 0 ? (double.NaN, double.NaN) : (meanAcc / used, bandAcc / used);
    }

    private static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s[s.Count / 2];
    }

    /// <summary>
    /// Ceiling on <c>--permutations</c> for this command. Reported to the operator by
    /// <see cref="LabStats.ReportPermutationCap"/> at the top of the run, so a request for
    /// more than this is visibly not what was executed.
    /// </summary>
    private const int PermutationCap = 20_000;

    /// <summary>
    /// Two-sample permutation test — see <see cref="LabStats.PermutationP"/>. The seed lives here,
    /// not in the shared helper, because it is this command's research parameter.
    /// Capped at 20,000 permutations: this command runs the test inside a loop over
    /// many buckets, and the full count would dominate its runtime.
    /// </summary>
    private static double PermutationP(double[] pool, int nA, int nB, double observed, int runs) =>
        LabStats.PermutationP(pool, nA, nB, observed, runs, seed: 9090, cap: PermutationCap);
}
