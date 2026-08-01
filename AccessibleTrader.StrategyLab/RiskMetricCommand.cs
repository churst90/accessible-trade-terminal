using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// The 0–1 "risk metric" — does rank-scaling price extension add anything, and how much does the
/// published version flatter itself?
///
/// <para>
/// THE CONSTRUCTION under every rainbow risk chart: take price relative to a long moving average,
/// normalise it to 0–1 over the asset's history, colour the bands. Low = "low risk", high = "high
/// risk". It fits this project's own standing principle — ranks and ratios rather than levels —
/// which is exactly why it deserves a real test rather than agreement.
/// </para>
///
/// <para>THREE THINGS ARE MEASURED, and the first is the one nobody publishes.</para>
/// <list type="number">
///   <item>
///     <b>The lookahead premium.</b> The honest metric ranks today's extension against ONLY prior
///     history (expanding window). The published version ranks it against the whole series including
///     the future. Both are computed here and reported side by side, so the cost of doing it
///     properly is a number rather than an argument.
///   </item>
///   <item>
///     <b>Does the rank transform beat its own raw input?</b> The metric is a price/MA transform. If
///     the plain distance from the moving average predicts as well, the normalisation is decoration.
///     This is the control that killed the on-chain valuation metrics: monotone at p = 0.0002 until
///     the matched price/SMA baseline gave p = 0.9855.
///   </item>
///   <item>
///     <b>Is it monotone?</b> A risk metric worth the name should show forward returns falling as
///     risk rises — not just a difference between the extremes, which one outlier decade can produce.
///   </item>
/// </list>
/// </summary>
public static class RiskMetricCommand
{
    private const int MaLength = 200;
    private const int MinHistory = 400;    // before this the expanding rank has nothing to rank against

    private sealed record Point(double Expanding, double FullSample, double RawZ, double Fwd);

    public static int Run(string snapshotDir, string tf, int horizon)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var byClass = new Dictionary<string, List<Point>>(StringComparer.OrdinalIgnoreCase);
        int instruments = 0;

        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            if (snap.Bars.Count < MinHistory + horizon + MaLength) continue;
            string cls = Path.GetFileName(f).StartsWith("bitstamp") || Path.GetFileName(f).StartsWith("mexc")
                ? "crypto" : "equities";

            var pts = Build(snap.Bars.ToArray(), horizon);
            if (pts.Count < 200) continue;
            if (!byClass.TryGetValue(cls, out var l)) byClass[cls] = l = new();
            l.AddRange(pts);
            instruments++;
        }

        if (byClass.Count == 0) { Console.Error.WriteLine("Not enough history anywhere."); return 1; }

        Console.WriteLine();
        Console.WriteLine("═════ THE 0–1 RISK METRIC ═════");
        Console.WriteLine($"{instruments} instruments · {tf} · forward horizon {horizon} bars · MA({MaLength}) in log space");
        Console.WriteLine();
        Console.WriteLine("Metric = rank of log(price / MA) within its own history, scaled 0–1.");
        Console.WriteLine("  EXPANDING = ranked against prior bars only (honest).");
        Console.WriteLine("  FULL-SAMPLE = ranked against the whole series (what a published chart shows).");
        Console.WriteLine("  RAW = the plain log(price / MA) it is built from — the baseline it must beat.");
        Console.WriteLine();

        foreach (var (cls, pts) in byClass.OrderBy(k => k.Key))
        {
            Console.WriteLine($"── {cls.ToUpperInvariant()} — {pts.Count:N0} bars " + new string('─', 38));
            Deciles("EXPANDING (honest)", pts, p => p.Expanding);
            Deciles("FULL-SAMPLE (lookahead)", pts, p => p.FullSample);
            Deciles("RAW log(price/MA)", pts, p => p.RawZ);
            Console.WriteLine();
        }

        Console.WriteLine("Reading it: compare the three spreads within an asset class. If FULL-SAMPLE is much");
        Console.WriteLine("wider than EXPANDING, the published picture is borrowing from the future. If RAW is as");
        Console.WriteLine("wide as EXPANDING, the 0–1 normalisation is presentation rather than information.");
        return 0;
    }

    private static List<Point> Build(Ohlcv[] bars, int horizon)
    {
        int n = bars.Length;
        var ext = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (i < MaLength) { ext[i] = double.NaN; continue; }
            double sum = 0;
            for (int k = i - MaLength + 1; k <= i; k++) sum += Math.Log(bars[k].Close);
            ext[i] = Math.Log(bars[i].Close) - sum / MaLength;
        }

        // Full-sample ranks: the lookahead version, computed once over everything.
        var valid = Enumerable.Range(0, n).Where(i => !double.IsNaN(ext[i])).ToList();
        var sortedAll = valid.Select(i => ext[i]).OrderBy(x => x).ToArray();

        var pts = new List<Point>();
        for (int i = MaLength + MinHistory; i < n - horizon; i++)
        {
            if (double.IsNaN(ext[i])) continue;

            // Expanding rank: how many PRIOR values were below today's. Only past data.
            int below = 0, count = 0;
            for (int k = MaLength; k < i; k++)
            {
                if (double.IsNaN(ext[k])) continue;
                count++;
                if (ext[k] < ext[i]) below++;
            }
            if (count < MinHistory) continue;

            double expanding = below / (double)count;
            double full = (Array.BinarySearch(sortedAll, ext[i]) is int idx && idx >= 0 ? idx
                            : ~Array.BinarySearch(sortedAll, ext[i])) / (double)sortedAll.Length;
            double fwd = Math.Log(bars[i + horizon].Close) - Math.Log(bars[i].Close);

            pts.Add(new Point(expanding, full, ext[i], fwd));
        }
        return pts;
    }

    private static void Deciles(string label, List<Point> pts, Func<Point, double> key)
    {
        var ordered = pts.OrderBy(key).ToList();
        int per = ordered.Count / 10;
        if (per < 10) return;

        var means = new double[10];
        for (int d = 0; d < 10; d++)
        {
            int from = d * per;
            int to = d == 9 ? ordered.Count : (d + 1) * per;
            means[d] = ordered.Skip(from).Take(to - from).Average(p => p.Fwd);
        }

        double spread = means[0] - means[9];       // low risk minus high risk; should be positive
        int monotoneSteps = 0;
        for (int d = 1; d < 10; d++) if (means[d] <= means[d - 1]) monotoneSteps++;

        Console.WriteLine($"  {label,-26} low-decile {means[0] * 100,+6:0.0}%  high-decile {means[9] * 100,+6:0.0}%  "
                        + $"spread {spread * 100,+6:0.0}pts  monotone {monotoneSteps}/9 steps");
    }
}
