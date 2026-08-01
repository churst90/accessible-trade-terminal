using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Does the WAY price approaches a level predict whether the level holds?
///
/// <para>
/// THE CLAIM, from the 2026-08-01 trader's-triangle interview, in two halves that pull against each
/// other. One: "if the price is just going crazy into my supply and demand, momentum is against me,
/// I'm going to shave off my risk" — a one-way approach means the level is more likely to break.
/// Two: "I don't like when the price hangs around my liquidity too long, because that ranging is
/// just accumulation of stop losses" — a level price has loitered at is more likely to break too.
/// Both halves are measurable and they are measured separately here.
/// </para>
///
/// <para>
/// THE CONTROL, and without it none of this means anything: <b>matched random levels</b>. The fib
/// study measured that a RANDOM horizontal line is respected about 59% of the time, because you only
/// touch a level from one side and "did price come back" is mostly a statement about the measurement
/// geometry. So a conditional respect rate of 62% is not evidence of anything. What has to survive
/// is the <i>difference of differences</i>: does the conditioning move real levels MORE than it moves
/// random lines drawn at the same density on the same bars?
/// </para>
///
/// <para>
/// NO LOOKAHEAD: a swing pivot at bar p is confirmed only at p+span, and a level becomes eligible to
/// be touched only after that. Touch and outcome windows never overlap the confirmation window.
/// </para>
/// </summary>
public static class ApproachCommand
{
    private const double TouchAtr = 0.25;    // within a quarter ATR counts as a touch
    private const double TravelAtr = 1.0;    // "respected" = travels this far back
    private const double ThroughAtr = 1.0;   // "broken" = closes this far through
    private const int OutcomeBars = 10;      // ... within this many bars
    private const int ApproachBars = 12;     // the window whose character we measure
    private const int PivotSpan = 10;

    private sealed record Touch(bool Respected, double Efficiency, int BarsNear);

    private sealed record Bucket(string Label, int N, double Hold)
    {
        public static Bucket From(string label, List<Touch> t) =>
            new(label, t.Count, t.Count == 0 ? double.NaN : t.Count(x => x.Respected) / (double)t.Count);
    }

    public static int Run(string snapshotDir, string? only, string tf)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0) { Console.Error.WriteLine($"No {tf} snapshots matched."); return 1; }

        var real = new List<Touch>();
        var random = new List<Touch>();
        int instruments = 0;

        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            if (snap.Bars.Count < 500) continue;
            var bars = snap.Bars.ToArray();
            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars, 14);

            var levels = SwingLevels(bars);
            if (levels.Count < 5) continue;

            real.AddRange(Collect(bars, atr, levels));
            random.AddRange(Collect(bars, atr, RandomLevels(bars, levels.Count, StableSeed(f))));
            instruments++;
        }

        Report(real, random, instruments, tf);
        return 0;
    }

/// <summary>
    /// A DETERMINISTIC seed from a string. string.GetHashCode() is randomised per process in .NET,
    /// so seeding a control with it makes the control resample on every run — and a p-value that
    /// moves between runs is not a p-value. This bit us: the same bucket read -5.6 and then -1.8 on
    /// two consecutive runs of the same code. FNV-1a, fixed forever.
    /// </summary>
    private static int StableSeed(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    // ── Measurement ─────────────────────────────────────────────────────────────

    /// <summary>Confirmed swing pivots. A pivot at p is only known at p+span, and is returned with that date.</summary>
    private static List<(int ConfirmedAt, double Price)> SwingLevels(Ohlcv[] bars)
    {
        var levels = new List<(int, double)>();
        for (int i = PivotSpan; i < bars.Length - PivotSpan; i++)
        {
            bool isHigh = true, isLow = true;
            for (int k = 1; k <= PivotSpan; k++)
            {
                if (bars[i].High < bars[i - k].High || bars[i].High < bars[i + k].High) isHigh = false;
                if (bars[i].Low > bars[i - k].Low || bars[i].Low > bars[i + k].Low) isLow = false;
            }
            if (isHigh) levels.Add((i + PivotSpan, bars[i].High));
            if (isLow) levels.Add((i + PivotSpan, bars[i].Low));
        }
        return levels;
    }

    /// <summary>
    /// The control: the same NUMBER of horizontal lines, drawn uniformly across the same price range,
    /// each becoming eligible at the same time as a real level did. Matching the density and the
    /// eligibility schedule is what makes the comparison fair.
    /// </summary>
    private static List<(int ConfirmedAt, double Price)> RandomLevels(
        Ohlcv[] bars, int count, int seed)
    {
        var rng = new Random(seed);
        double lo = bars.Min(b => b.Low), hi = bars.Max(b => b.High);
        var outp = new List<(int, double)>(count);
        for (int i = 0; i < count; i++)
        {
            int at = PivotSpan * 2 + rng.Next(Math.Max(1, bars.Length - PivotSpan * 2 - OutcomeBars));
            // Uniform in LOG price: a uniform draw in raw price on an asset that went 100x would put
            // almost every random line above almost all of the history and never get touched.
            double t = rng.NextDouble();
            outp.Add((at, Math.Exp(Math.Log(lo) + t * (Math.Log(hi) - Math.Log(lo)))));
        }
        return outp;
    }

    private static List<Touch> Collect(Ohlcv[] bars, double[] atr, List<(int ConfirmedAt, double Price)> levels)
    {
        var touches = new List<Touch>();

        foreach (var (confirmedAt, price) in levels)
        {
            // One touch per level: the first eligible one. Counting every re-touch would weight
            // whichever levels price happened to sit on, which is the thing being measured.
            for (int i = Math.Max(confirmedAt, ApproachBars); i < bars.Length - OutcomeBars; i++)
            {
                double a = atr[i];
                if (double.IsNaN(a) || a <= 0) continue;
                if (Math.Abs(bars[i].Close - price) > TouchAtr * a) continue;

                bool fromBelow = bars[i - ApproachBars].Close < price;

                // Respected: travels TravelAtr back the way it came within OutcomeBars, without
                // first closing ThroughAtr through.
                bool respected = false;
                for (int k = 1; k <= OutcomeBars; k++)
                {
                    var c = bars[i + k].Close;
                    if (fromBelow)
                    {
                        if (c > price + ThroughAtr * a) break;
                        if (c < price - TravelAtr * a) { respected = true; break; }
                    }
                    else
                    {
                        if (c < price - ThroughAtr * a) break;
                        if (c > price + TravelAtr * a) { respected = true; break; }
                    }
                }

                // Approach character. Efficiency = net move / total path over the approach window:
                // 1.0 is a straight line into the level, near 0 is chop.
                double net = Math.Abs(bars[i].Close - bars[i - ApproachBars].Close);
                double path = 0;
                for (int k = i - ApproachBars + 1; k <= i; k++) path += Math.Abs(bars[k].Close - bars[k - 1].Close);
                double eff = path <= 0 ? 0 : net / path;

                // Loitering: bars in the approach window that sat within half an ATR of the level.
                int near = 0;
                for (int k = i - ApproachBars + 1; k <= i; k++)
                    if (Math.Abs(bars[k].Close - price) <= 0.5 * a) near++;

                touches.Add(new Touch(respected, eff, near));
                break;
            }
        }
        return touches;
    }

    // ── Reporting ───────────────────────────────────────────────────────────────

    private static void Report(List<Touch> real, List<Touch> random, int instruments, string tf)
    {
        Console.WriteLine();
        Console.WriteLine("═════ DOES THE APPROACH PREDICT WHETHER A LEVEL HOLDS? ═════");
        Console.WriteLine($"{instruments} instruments · {tf} · {real.Count:N0} real-level touches · {random.Count:N0} random-line touches");
        Console.WriteLine();
        Console.WriteLine("Level = confirmed swing pivot (knowable only span bars later). Touch = close within");
        Console.WriteLine($"{TouchAtr} ATR. Respected = travels {TravelAtr} ATR back within {OutcomeBars} bars without closing");
        Console.WriteLine($"{ThroughAtr} ATR through. CONTROL = the same number of random horizontal lines, matched density.");
        Console.WriteLine();

        Console.WriteLine($"  baseline hold: real {Pct(Hold(real))}   random {Pct(Hold(random))}   difference {Diff(Hold(real), Hold(random))}");
        Console.WriteLine();

        // Half A: one-way vs two-way approach.
        Console.WriteLine("── A. Approach efficiency (1.0 = straight line in, 0 = chop) " + new string('─', 18));
        Console.WriteLine($"{"bucket",-18}{"real n",8}{"real hold",11}{"rand n",8}{"rand hold",11}{"real-rand",11}{"p",9}");
        foreach (var (label, lo, hi) in new[] { ("chop (<0.25)", 0.0, 0.25), ("middle", 0.25, 0.5), ("one-way (>0.5)", 0.5, 1.01) })
        {
            var r = real.Where(t => t.Efficiency >= lo && t.Efficiency < hi).ToList();
            var c = random.Where(t => t.Efficiency >= lo && t.Efficiency < hi).ToList();
            Row(label, r, c);
        }
        Console.WriteLine();

        // Half B: loitering.
        Console.WriteLine("── B. Bars spent within half an ATR of the level before the touch " + new string('─', 13));
        Console.WriteLine($"{"bucket",-18}{"real n",8}{"real hold",11}{"rand n",8}{"rand hold",11}{"real-rand",11}{"p",9}");
        foreach (var (label, lo, hi) in new[] { ("clean (0-1)", 0, 2), ("some (2-4)", 2, 5), ("loitering (5+)", 5, 999) })
        {
            var r = real.Where(t => t.BarsNear >= lo && t.BarsNear < hi).ToList();
            var c = random.Where(t => t.BarsNear >= lo && t.BarsNear < hi).ToList();
            Row(label, r, c);
        }
        Console.WriteLine();

        Console.WriteLine("Reading it: the last column is the only one that can carry a finding. A real-level hold");
        Console.WriteLine("rate that rises across buckets means nothing if the random lines rise the same way —");
        Console.WriteLine("that would be the conditioning describing the measurement, not the level.");
    }

    private static void Row(string label, List<Touch> r, List<Touch> c)
    {
        double hr = Hold(r), hc = Hold(c);
        Console.WriteLine($"{label,-18}{r.Count,8}{Pct(hr),11}{c.Count,8}{Pct(hc),11}{Diff(hr, hc),11}{PDiff(r, c),9}");
    }

    /// <summary>
    /// Two-proportion z-test on real-versus-random within the bucket. Reported because a five-point
    /// gap on six hundred samples and a five-point gap on six thousand are different findings, and
    /// the eye cannot tell them apart in a table.
    /// </summary>
    private static string PDiff(List<Touch> r, List<Touch> c)
    {
        if (r.Count < 30 || c.Count < 30) return "n/a";
        double p1 = Hold(r), p2 = Hold(c);
        double pool = (r.Count(x => x.Respected) + c.Count(x => x.Respected)) / (double)(r.Count + c.Count);
        double se = Math.Sqrt(pool * (1 - pool) * (1.0 / r.Count + 1.0 / c.Count));
        if (se <= 0) return "n/a";
        double z = (p1 - p2) / se;
        return $"{2 * (1 - NormCdf(Math.Abs(z))):0.000}";
    }

    private static double NormCdf(double z)
    {
        // Abramowitz & Stegun 7.1.26 — plenty for a p-value printed to three places.
        double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(z));
        double d = 0.3989423 * Math.Exp(-z * z / 2);
        double p = d * t * (0.3193815 + t * (-0.3565638 + t * (1.781478 + t * (-1.821256 + t * 1.330274))));
        return z > 0 ? 1 - p : p;
    }

    private static double Hold(List<Touch> t) =>
        t.Count == 0 ? double.NaN : t.Count(x => x.Respected) / (double)t.Count;

    private static string Pct(double d) => double.IsNaN(d) ? "n/a" : $"{d * 100:0.0}%";

    private static string Diff(double a, double b) =>
        double.IsNaN(a) || double.IsNaN(b) ? "n/a" : $"{(a - b) * 100:+0.0;-0.0}";
}
