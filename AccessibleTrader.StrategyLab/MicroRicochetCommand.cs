using System.Globalization;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the ONE Cosasverdes claim the daily-resolution work could not touch: the "micro candle
/// ricochet" / monodirectional-interaction system.
///
/// <para>
/// It is explicitly a layer-2 system. Layer 1 (your own TA) picks WHICH level matters; this
/// claims to tell you, by zooming to seconds, whether that level is about to hold. The three
/// outcomes carry a point score: a clean ricochet is 2, a penetrate-then-reclaim is 1, and
/// passing straight through is 0. The falsifiable prediction is an ORDERING — trades taken after
/// a ricochet should outperform reclaims, which should outperform throughs.
/// </para>
///
/// <para>
/// NO LOOKAHEAD. Classification uses only the micro-window bars; the trade is entered at the
/// CLOSE of the final micro-window bar and resolved by walking forward one minute at a time.
/// Stop and target are fixed before the walk begins.
/// </para>
///
/// <para>
/// THE CONTROL is a label permutation test. The realised R-multiples are held fixed and the
/// ricochet/reclaim/through labels are reshuffled thousands of times; the observed
/// ricochet-minus-through gap is compared against that null distribution. This asks exactly the
/// right question — does the CLASSIFICATION carry information — rather than the much easier
/// question of whether trading levels at all is profitable.
/// </para>
/// </summary>
public static class MicroRicochetCommand
{
    /// <summary>Minutes of micro-structure examined after price first reaches the level.</summary>
    private const int MicroWindowMinutes = 15;

    /// <summary>Minutes allowed for the trade to reach its stop or target.</summary>
    private const int TradeHorizonMinutes = 240;

    /// <summary>Risk unit as a fraction of the level-timeframe ATR.</summary>
    private const double RiskAtrFraction = 0.5;

    private const double TargetR = 2.0;

    private sealed record Event(InteractionKind Kind, bool Long, double R, string Level);

    public static Task<int> RunAsync(string csvDir, string levelTf, int permutations,
        DateTime? from = null, DateTime? to = null)
    {
        if (!Directory.Exists(csvDir)) { Console.Error.WriteLine($"Not found: {csvDir}"); return Task.FromResult(1); }

        Console.WriteLine("Loading 1-minute bars…");
        var minutes = LoadBinanceCsvs(csvDir);
        if (from.HasValue) minutes = minutes.Where(b => b.Date >= from.Value).ToList();
        if (to.HasValue) minutes = minutes.Where(b => b.Date < to.Value).ToList();
        if (minutes.Count < 10000) { Console.Error.WriteLine($"Only {minutes.Count} bars."); return Task.FromResult(1); }
        Console.WriteLine($"  {minutes.Count:N0} bars, {minutes[0].Date:yyyy-MM-dd} → {minutes[^1].Date:yyyy-MM-dd}");

        var resampler = new ResamplerService();
        var htf = resampler.Resample(minutes, levelTf);
        Console.WriteLine($"  {htf.Count:N0} {levelTf} bars for level definition");

        int minutesPerHtf = (int)(TimeframeUtility.ToMilliseconds(levelTf) / 60000);
        var htfAtr = IndicatorMath.Atr(htf.ToArray(), 14);

        // Index 1-minute bars by their level-timeframe bucket so each touch can be zoomed into.
        var bucketStart = new Dictionary<long, int>();
        for (int i = 0; i < minutes.Count; i++)
        {
            long key = TimeframeUtility.GetPeriodStart(minutes[i].Date, levelTf).Ticks;
            if (!bucketStart.ContainsKey(key)) bucketStart[key] = i;
        }

        var levels = BuildLevels(htf, levelTf);
        Console.WriteLine($"  {levels.Count} level candidates");

        var events = new List<Event>();

        foreach (var (label, values) in levels)
        {
            int lastCounted = -1;
            for (int i = 1; i < htf.Count; i++)
            {
                double level = values[i];
                double atr = htfAtr[i];
                if (double.IsNaN(level) || double.IsNaN(atr) || atr <= 0) continue;
                if (lastCounted >= 0 && i - lastCounted < 5) continue;

                double tol = atr * 0.25;
                if (!(htf[i].Low <= level + tol && htf[i].High >= level - tol)) continue;

                double prevClose = htf[i - 1].Close;
                if (Math.Abs(prevClose - level) < 1e-9) continue;
                bool fromAbove = prevClose > level;   // testing support → a long setup

                if (!bucketStart.TryGetValue(htf[i].Date.Ticks, out int m0)) continue;

                // First minute bar that actually reaches the band.
                int touchMinute = -1;
                int scanEnd = Math.Min(minutes.Count - 1, m0 + minutesPerHtf);
                for (int m = m0; m <= scanEnd; m++)
                    if (minutes[m].Low <= level + tol && minutes[m].High >= level - tol) { touchMinute = m; break; }
                if (touchMinute < 0) continue;

                int windowEnd = touchMinute + MicroWindowMinutes;
                if (windowEnd + TradeHorizonMinutes >= minutes.Count) continue;

                // ── Classify using ONLY the micro window ──────────────────────
                double microTol = atr * 0.10;
                bool penetrated = false;
                for (int m = touchMinute; m <= windowEnd; m++)
                    if (fromAbove ? minutes[m].Low < level : minutes[m].High > level) { penetrated = true; break; }

                double endClose = minutes[windowEnd].Close;
                bool endedThrough = fromAbove ? endClose < level - microTol : endClose > level + microTol;

                var kind = endedThrough ? InteractionKind.Through
                    : penetrated ? InteractionKind.Reclaim
                    : InteractionKind.Ricochet;

                // ── Trade from the window's close, in the defending direction ──
                bool goLong = fromAbove;
                double entry = endClose;
                double risk = atr * RiskAtrFraction;
                if (risk <= 0) continue;

                double stop = goLong ? entry - risk : entry + risk;
                double target = goLong ? entry + risk * TargetR : entry - risk * TargetR;

                double r = 0;
                bool resolved = false;
                for (int m = windowEnd + 1; m <= windowEnd + TradeHorizonMinutes; m++)
                {
                    var b = minutes[m];
                    // Stop checked first: when a single minute spans both, assume the worse fill.
                    if (goLong ? b.Low <= stop : b.High >= stop) { r = -1; resolved = true; break; }
                    if (goLong ? b.High >= target : b.Low <= target) { r = TargetR; resolved = true; break; }
                }
                if (!resolved)
                {
                    double exit = minutes[windowEnd + TradeHorizonMinutes].Close;
                    r = (goLong ? exit - entry : entry - exit) / risk;
                }

                events.Add(new Event(kind, goLong, r, label));
                lastCounted = i;
            }
        }

        Report(events, levelTf, permutations);
        return Task.FromResult(0);
    }

    private static void Report(List<Event> events, string levelTf, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine($"===== MICRO RICOCHET — {events.Count:N0} level touches on {levelTf} levels =====");
        Console.WriteLine($"Classified over {MicroWindowMinutes} minutes, then traded {TargetR}R target / 1R stop,");
        Console.WriteLine($"{TradeHorizonMinutes} minutes max. Entry at the close of the classification window.");
        Console.WriteLine();

        if (events.Count < 100) { Console.WriteLine("Too few events."); return; }

        Console.WriteLine($"  {"class",-10} {"n",6} {"share",7} {"win%",6} {"meanR",7} {"medR",7} {"stderr",7}");
        foreach (var kind in new[] { InteractionKind.Ricochet, InteractionKind.Reclaim, InteractionKind.Through })
        {
            var g = events.Where(e => e.Kind == kind).ToList();
            if (g.Count == 0) continue;
            double mean = g.Average(e => e.R);
            double sd = Math.Sqrt(g.Sum(e => (e.R - mean) * (e.R - mean)) / Math.Max(1, g.Count - 1));
            Console.WriteLine($"  {kind,-10} {g.Count,6} {(double)g.Count / events.Count,6:P0} " +
                              $"{g.Count(e => e.R > 0) / (double)g.Count,5:P0} {mean,7:+0.000;-0.000;0} " +
                              $"{Median(g.Select(e => e.R).ToList()),7:+0.000;-0.000;0} {sd / Math.Sqrt(g.Count),7:0.000}");
        }

        double all = events.Average(e => e.R);
        Console.WriteLine($"  {"ALL",-10} {events.Count,6} {"",7} {events.Count(e => e.R > 0) / (double)events.Count,5:P0} {all,7:+0.000;-0.000;0}");

        // ── Permutation test on the ordering ─────────────────────────────────
        var ric = events.Where(e => e.Kind == InteractionKind.Ricochet).ToList();
        var thr = events.Where(e => e.Kind == InteractionKind.Through).ToList();
        if (ric.Count < 30 || thr.Count < 30) { Console.WriteLine("\n  Too few in a class for the permutation test."); return; }

        double observed = ric.Average(e => e.R) - thr.Average(e => e.R);
        var pool = events.Select(e => e.R).ToArray();
        int nRic = ric.Count, nThr = thr.Count;
        var rng = new Random(12345);
        int atLeastAsExtreme = 0;

        for (int p = 0; p < permutations; p++)
        {
            Shuffle(pool, rng);
            double a = 0, b = 0;
            for (int i = 0; i < nRic; i++) a += pool[i];
            for (int i = nRic; i < nRic + nThr; i++) b += pool[i];
            double gap = a / nRic - b / nThr;
            if (Math.Abs(gap) >= Math.Abs(observed)) atLeastAsExtreme++;
        }

        double pValue = (atLeastAsExtreme + 1.0) / (permutations + 1.0);
        Console.WriteLine();
        Console.WriteLine($"  PERMUTATION TEST ({permutations:N0} shuffles of the class labels):");
        Console.WriteLine($"    ricochet mean R − through mean R = {observed:+0.0000;-0.0000;0}");
        Console.WriteLine($"    two-sided p = {pValue:0.0000}");
        Console.WriteLine(pValue <= 0.05
            ? "    → the classification carries information."
            : "    → the classification is indistinguishable from random labels.");

        Console.WriteLine();
        Console.WriteLine("  By level type (each with its OWN permutation test; note that scanning 7 level");
        Console.WriteLine("  types for the best gap is itself a multiple-comparison problem):");
        foreach (var g in events.GroupBy(e => e.Level).OrderByDescending(g => g.Count()))
        {
            var r2 = g.Where(e => e.Kind == InteractionKind.Ricochet).ToList();
            var t2 = g.Where(e => e.Kind == InteractionKind.Through).ToList();
            if (r2.Count < 10 || t2.Count < 10) continue;
            double gap = r2.Average(e => e.R) - t2.Average(e => e.R);
            double p = PermutationP(g.Select(e => e.R).ToArray(), r2.Count, t2.Count, gap, permutations);
            Console.WriteLine($"    {g.Key,-16} n={g.Count(),5}  ric={r2.Average(e => e.R),7:+0.000;-0.000;0} " +
                              $"thru={t2.Average(e => e.R),7:+0.000;-0.000;0}  gap={gap,7:+0.000;-0.000;0}  p={p,6:0.000}");
        }
    }

    // ── Level construction ────────────────────────────────────────────────────

    private static List<(string Label, double[] Values)> BuildLevels(List<Ohlcv> htf, string tf)
    {
        var result = new List<(string, double[])>();
        var closes = htf.Select(b => b.Close).ToArray();

        foreach (int period in new[] { 21, 89, 200 })
            result.Add(($"EMA{period}", AccessibleTrader.Core.Services.Indicators.MovingAverageHelper
                .Calculate(closes, period, "EMA")));

        // Prior-day high and low — stepped, so no future leak.
        var dayHigh = new double[htf.Count];
        var dayLow = new double[htf.Count];
        Array.Fill(dayHigh, double.NaN);
        Array.Fill(dayLow, double.NaN);
        DateTime curDay = DateTime.MinValue;
        double prevH = double.NaN, prevL = double.NaN, runH = double.MinValue, runL = double.MaxValue;
        for (int i = 0; i < htf.Count; i++)
        {
            var d = htf[i].Date.Date;
            if (d != curDay)
            {
                if (curDay != DateTime.MinValue) { prevH = runH; prevL = runL; }
                curDay = d; runH = double.MinValue; runL = double.MaxValue;
            }
            dayHigh[i] = prevH; dayLow[i] = prevL;
            runH = Math.Max(runH, htf[i].High);
            runL = Math.Min(runL, htf[i].Low);
        }
        result.Add(("prevDayHigh", dayHigh));
        result.Add(("prevDayLow", dayLow));

        // Round numbers: the nearest 1000-dollar step above and below each bar.
        var roundUp = new double[htf.Count];
        var roundDown = new double[htf.Count];
        for (int i = 0; i < htf.Count; i++)
        {
            double step = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(htf[i].Close, 1))) - 1);
            roundUp[i] = Math.Ceiling(htf[i].Close / step) * step;
            roundDown[i] = Math.Floor(htf[i].Close / step) * step;
        }
        result.Add(("roundAbove", roundUp));
        result.Add(("roundBelow", roundDown));

        return result;
    }

    // ── Binance Vision CSV loading ────────────────────────────────────────────

    public static List<Ohlcv> LoadBinanceCsvs(string dir)
    {
        var bars = new List<Ohlcv>(2_000_000);
        foreach (var file in Directory.GetFiles(dir, "*.csv").OrderBy(f => f))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0) continue;
                var p = line.Split(',');
                if (p.Length < 6) continue;
                // Newer archives carry a header row; skip anything unparseable.
                if (!long.TryParse(p[0], out long openMs)) continue;
                if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double o)) continue;
                double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double h);
                double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double l);
                double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double c);
                double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double v);

                // Archives switched from milliseconds to microseconds partway through 2025.
                if (openMs > 100_000_000_000_000L) openMs /= 1000;
                bars.Add(new Ohlcv(DateTimeOffset.FromUnixTimeMilliseconds(openMs).UtcDateTime, o, h, l, c, v));
            }
        }
        bars.Sort((a, b) => a.Date.CompareTo(b.Date));
        return bars;
    }

    /// <summary>
    /// Two-sample permutation test — see <see cref="LabStats.PermutationP"/>. The seed lives here,
    /// not in the shared helper, because it is this command's research parameter.
    /// </summary>
    private static double PermutationP(double[] pool, int nA, int nB, double observed, int runs) =>
        LabStats.PermutationP(pool, nA, nB, observed, runs, seed: 999);
    private static void Shuffle(double[] a, Random rng)
    {
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    private static double Median(List<double> v)
    {
        if (v.Count == 0) return double.NaN;
        var s = v.OrderBy(x => x).ToList();
        int m = s.Count / 2;
        return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2;
    }
}
