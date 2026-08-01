using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Sweep and reclaim: does breaching a cluster of equal highs and then CLOSING back inside carry
/// information?
///
/// <para>
/// THE CLAIM: "you have a series of equal highs, a move up, and then back into the range — that move
/// back into the range is a confirmation of my entry … I need the closed candle, not the open
/// candle." Of everything in that interview this is the one piece of liquidity language that is
/// algorithmically definable, which is why it is testable at all: a cluster of highs within a
/// tolerance, a breach of it, and a close back below it inside a few bars.
/// </para>
///
/// <para>TWO CONTROLS, and the second is the one that isolates the claim.</para>
/// <list type="number">
///   <item>
///     <b>Random entries</b>, same count, same direction, drawn from the same bars — the floor any
///     entry has to clear.
///   </item>
///   <item>
///     <b>Breach without reclaim.</b> The same setups, entered at the breach and never waiting for
///     the close back inside. He is explicit that the reclaim is the part that matters, so this is
///     the arm that tests HIS claim rather than "do sweeps mean something".
///   </item>
/// </list>
///
/// <para>
/// Outcome is a fixed 20-bar forward move in R (risk = 1 ATR at entry), deliberately: an exit rule
/// here would test the exit, and this lab has already measured that exits change results more than
/// entries do.
/// </para>
/// </summary>
public static class SweepReclaimCommand
{
    private const int PivotSpan = 5;
    private const double ClusterAtr = 0.35;   // how close two highs must be to count as "equal"
    private const double BreachAtr = 0.10;    // how far past the level counts as a breach
    private const int ReclaimBars = 3;        // close back inside within this many bars
    private const int Horizon = 20;

    private sealed record Signal(int Bar, bool Short, double R);

    public static int Run(string snapshotDir, string tf)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var reclaim = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        var breachOnly = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        var random = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        int instruments = 0;

        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            if (snap.Bars.Count < 500) continue;
            var bars = snap.Bars.ToArray();
            string cls = Path.GetFileName(f).StartsWith("bitstamp") || Path.GetFileName(f).StartsWith("mexc")
                ? "crypto" : "equities";

            var (rec, br) = Collect(bars);
            if (rec.Count == 0 && br.Count == 0) continue;

            Add(reclaim, cls, rec.Select(s => s.R));
            Add(breachOnly, cls, br.Select(s => s.R));
            Add(random, cls, RandomArm(bars, br, StableSeed(f)));
            instruments++;
        }

        if (reclaim.Count == 0) { Console.Error.WriteLine("No setups found."); return 1; }

        Console.WriteLine();
        Console.WriteLine("═════ SWEEP AND RECLAIM ═════");
        Console.WriteLine($"{instruments} instruments · {tf} · outcome = {Horizon}-bar forward move in R (risk 1 ATR)");
        Console.WriteLine();
        Console.WriteLine("Setup: >=2 swing highs within " + ClusterAtr + " ATR of each other (confirmed, no lookahead),");
        Console.WriteLine("breached by " + BreachAtr + " ATR, then a CLOSE back inside within " + ReclaimBars + " bars. Short. Mirrored for lows.");
        Console.WriteLine();
        Console.WriteLine($"  {"class",-10}{"arm",-22}{"n",8}{"mean R",10}{"win rate",10}");

        foreach (var cls in reclaim.Keys.OrderBy(k => k))
        {
            Row(cls, "sweep + reclaim", reclaim[cls]);
            Row(cls, "breach only (control)", breachOnly[cls]);
            Row(cls, "random entries (floor)", random[cls]);

            if (reclaim[cls].Count >= 30 && breachOnly[cls].Count >= 30)
                Console.WriteLine($"  {"",-10}reclaim − breach: {reclaim[cls].Average() - breachOnly[cls].Average():+0.000;-0.000} R"
                                + $"   (the claim is that this is positive)");
            Console.WriteLine();
        }

        Console.WriteLine("Reading it: 'reclaim − breach' isolates what he says matters — waiting for the close back");
        Console.WriteLine("inside instead of taking the breach. Both arms must also clear the random floor.");
        return 0;
    }

    private static void Row(string cls, string arm, List<double> v)
    {
        if (v.Count == 0) { Console.WriteLine($"  {cls,-10}{arm,-22}{0,8}{"—",10}{"—",10}"); return; }
        Console.WriteLine($"  {cls,-10}{arm,-22}{v.Count,8}{v.Average(),10:+0.000;-0.000}{v.Count(x => x > 0) / (double)v.Count * 100,9:0.0}%");
    }

    private static void Add(Dictionary<string, List<double>> d, string cls, IEnumerable<double> v)
    {
        if (!d.TryGetValue(cls, out var l)) d[cls] = l = new();
        l.AddRange(v);
    }

    // ── Setup detection ─────────────────────────────────────────────────────────

    private static (List<Signal> Reclaim, List<Signal> BreachOnly) Collect(Ohlcv[] bars)
    {
        var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars, 14);
        var reclaim = new List<Signal>();
        var breachOnly = new List<Signal>();

        // Confirmed swing pivots only: a pivot at p is knowable at p + span.
        var highs = new List<(int At, double P)>();
        var lows = new List<(int At, double P)>();
        for (int i = PivotSpan; i < bars.Length - PivotSpan; i++)
        {
            bool hi = true, lo = true;
            for (int k = 1; k <= PivotSpan; k++)
            {
                if (bars[i].High < bars[i - k].High || bars[i].High < bars[i + k].High) hi = false;
                if (bars[i].Low > bars[i - k].Low || bars[i].Low > bars[i + k].Low) lo = false;
            }
            if (hi) highs.Add((i + PivotSpan, bars[i].High));
            if (lo) lows.Add((i + PivotSpan, bars[i].Low));
        }

        Scan(bars, atr, highs, isHighCluster: true, reclaim, breachOnly);
        Scan(bars, atr, lows, isHighCluster: false, reclaim, breachOnly);
        return (reclaim, breachOnly);
    }

    private static void Scan(Ohlcv[] bars, double[] atr, List<(int At, double P)> pivots,
                             bool isHighCluster, List<Signal> reclaim, List<Signal> breachOnly)
    {
        for (int a = 1; a < pivots.Count; a++)
        {
            var (at, p) = pivots[a];
            var (prevAt, prevP) = pivots[a - 1];
            double atrAt = at < atr.Length ? atr[at] : double.NaN;
            if (double.IsNaN(atrAt) || atrAt <= 0) continue;

            // "Equal" highs/lows: two consecutive same-side pivots within tolerance. The cluster
            // level is the extreme of the two, which is where the stops sit.
            if (Math.Abs(p - prevP) > ClusterAtr * atrAt) continue;
            double level = isHighCluster ? Math.Max(p, prevP) : Math.Min(p, prevP);

            for (int i = at; i < bars.Length - Horizon - ReclaimBars; i++)
            {
                double v = atr[i];
                if (double.IsNaN(v) || v <= 0) continue;

                bool breached = isHighCluster
                    ? bars[i].High > level + BreachAtr * v
                    : bars[i].Low < level - BreachAtr * v;
                if (!breached) continue;

                // The breach-only arm enters here, at this close.
                breachOnly.Add(new Signal(i, isHighCluster, Forward(bars, i, v, isHighCluster)));

                // The reclaim arm waits for a CLOSE back inside within ReclaimBars.
                for (int k = 0; k <= ReclaimBars && i + k < bars.Length - Horizon; k++)
                {
                    bool back = isHighCluster ? bars[i + k].Close < level : bars[i + k].Close > level;
                    if (!back) continue;
                    reclaim.Add(new Signal(i + k, isHighCluster, Forward(bars, i + k, atr[i + k], isHighCluster)));
                    break;
                }
                break;   // one attempt per cluster
            }
        }
    }

    private static double Forward(Ohlcv[] bars, int i, double risk, bool isShort)
    {
        if (double.IsNaN(risk) || risk <= 0 || i + Horizon >= bars.Length) return 0;
        double move = (bars[i + Horizon].Close - bars[i].Close) / risk;
        return isShort ? -move : move;
    }

    /// <summary>Random entries matched in count and direction — the floor any entry must clear.</summary>
    private static List<double> RandomArm(Ohlcv[] bars, List<Signal> matched, int seed)
    {
        var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars, 14);
        var rng = new Random(seed);
        var outp = new List<double>(matched.Count);
        foreach (var m in matched)
        {
            int i = 20 + rng.Next(Math.Max(1, bars.Length - Horizon - 21));
            outp.Add(Forward(bars, i, atr[i], m.Short));
        }
        return outp;
    }

    private static int StableSeed(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }
}
