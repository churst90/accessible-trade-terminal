using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Do exits carry an edge, holding the entry fixed?
///
/// <para>
/// WHY THIS IS THE BIGGEST UNTESTED SURFACE. Every study in this lab so far has been about entries
/// or exposure. Two practitioners independently say that is the wrong emphasis, and Bobby Iaccino
/// tells the sharpest version of it: a floor trader nicknamed "the indicator" was wrong so reliably
/// that the desk tried fading every trade he made — and <b>the man fading him still lost money</b>,
/// "because he only followed his entries. He didn't follow his exits." A perfect contrarian entry
/// signal was not enough.
/// </para>
///
/// <para>
/// THE DESIGN. The entry is held completely fixed — the BTC trend rule, the only entry this lab has
/// validated out of sample. Only the exit varies. Any difference between books is therefore
/// attributable to the exit and nothing else.
/// </para>
///
/// <para>
/// THE CONTROL. A RANDOM exit, drawn from the same holding-period distribution the rule being tested
/// produces. This is the exposure-matched null applied to exits: it holds the number of bars in the
/// trade fixed and asks only whether the <i>timing</i> of the exit carries information. Without it,
/// an exit rule that simply holds longer in a rising asset looks skilful.
/// </para>
/// </summary>
public static class ExitCommand
{
    private const int ZWindow = 50;
    private const double EntryZ = 1.0;
    private const double ExitZ = 0.5;
    private const int MaxHold = 400;

    private sealed record Trade(int Entry, int Exit, double R, double Pct);
    private sealed record Result(string Name, double Equity, double MaxDd, double AvgR,
        double WinRate, double AvgBars, int N);

    public static int Run(string snapshotDir, string only, string tf, int permutations)
    {
        var f = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(x => !Path.GetFileName(x).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(x => Path.GetFileName(x).Contains(only, StringComparison.OrdinalIgnoreCase));
        if (f == null) { Console.WriteLine($"No snapshot for {only} {tf}"); return 1; }

        var bars = SnapshotCommand.Load(f).Bars;
        if (bars.Count < 600) { Console.WriteLine("Too few bars."); return 1; }

        var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);
        var entries = Entries(bars);

        Console.WriteLine();
        Console.WriteLine($"===== EXIT RULES — {only} {tf} =====");
        Console.WriteLine($"{bars.Count:N0} bars, {bars[0].Date:yyyy-MM} → {bars[^1].Date:yyyy-MM}. " +
                          $"{entries.Count} entries from the FIXED trend rule (z{ZWindow} crosses +{EntryZ}).");
        Console.WriteLine("Entry identical in every book. R = 1 ATR at entry. Only the exit varies.");
        Console.WriteLine();

        var rules = new (string Name, Func<int, double, IReadOnlyList<Ohlcv>, double[], int> Exit)[]
        {
            ("signal (z<0.5)",  (e, r, b, a) => SignalExit(e, b)),
            ("fixed 2R",        (e, r, b, a) => TargetStop(e, r, b, 2.0, 1.0)),
            ("fixed 3R",        (e, r, b, a) => TargetStop(e, r, b, 3.0, 1.0)),
            ("fixed 5R",        (e, r, b, a) => TargetStop(e, r, b, 5.0, 1.0)),
            ("ATR trail 3",     (e, r, b, a) => AtrTrail(e, b, a, 3.0)),
            ("ATR trail 5",     (e, r, b, a) => AtrTrail(e, b, a, 5.0)),
            ("time 20 bars",    (e, r, b, a) => Math.Min(e + 20, b.Count - 1)),
            ("time 60 bars",    (e, r, b, a) => Math.Min(e + 60, b.Count - 1)),
        };

        Console.WriteLine($"    {"exit rule",-16} {"equity",9} {"maxDD",7} {"avg R",7} {"win%",6} {"avg bars",9} {"vs RANDOM exit",15}");

        var results = new List<Result>();
        foreach (var (name, exit) in rules)
        {
            var trades = Build(bars, atr, entries, exit);
            if (trades.Count < 15) continue;
            var res = Score(name, bars, trades);
            results.Add(res);

            // The control: same trades, same holding-period distribution, random exit timing.
            double randEq = RandomExitEquity(bars, atr, entries, trades, permutations, out double pv);
            Console.WriteLine($"    {name,-16} {res.Equity,9:0.00}× {res.MaxDd,7:P0} {res.AvgR,7:+0.00;-0.00;0} " +
                              $"{res.WinRate,6:P0} {res.AvgBars,9:0.0} " +
                              $"{res.Equity / Math.Max(randEq, 1e-9),8:0.00}×  p={pv:0.000}{(pv <= 0.05 ? "*" : "")}");
        }

        Console.WriteLine();
        PartialScaleOut(bars, atr, entries, permutations);
        Verdict(results, bars, entries);
        return 0;
    }

    // ── Entry (fixed across every book) ──────────────────────────────────────

    private static List<int> Entries(IReadOnlyList<Ohlcv> bars)
    {
        var z = TradingCrossCommand.ZScore(bars, ZWindow);
        var outp = new List<int>();
        bool armed = true;
        for (int i = ZWindow + 2; i < bars.Count - 2; i++)
        {
            if (double.IsNaN(z[i]) || double.IsNaN(z[i - 1])) continue;
            if (armed && z[i - 1] <= EntryZ && z[i] > EntryZ) { outp.Add(i + 1); armed = false; }
            else if (!armed && z[i] < ExitZ) armed = true;
        }
        return outp;
    }

    // ── Exit rules ───────────────────────────────────────────────────────────

    private static int SignalExit(int entry, IReadOnlyList<Ohlcv> bars)
    {
        var z = TradingCrossCommand.ZScore(bars, ZWindow);
        for (int i = entry + 1; i < bars.Count && i < entry + MaxHold; i++)
            if (!double.IsNaN(z[i]) && z[i] < ExitZ) return i;
        return Math.Min(entry + MaxHold, bars.Count - 1);
    }

    private static int TargetStop(int entry, double r, IReadOnlyList<Ohlcv> bars, double target, double stop)
    {
        double e = bars[entry].Close;
        for (int i = entry + 1; i < bars.Count && i < entry + MaxHold; i++)
        {
            if (bars[i].Low <= e - r * stop) return i;
            if (bars[i].High >= e + r * target) return i;
        }
        return Math.Min(entry + MaxHold, bars.Count - 1);
    }

    private static int AtrTrail(int entry, IReadOnlyList<Ohlcv> bars, double[] atr, double mult)
    {
        double peak = bars[entry].Close;
        for (int i = entry + 1; i < bars.Count && i < entry + MaxHold; i++)
        {
            peak = Math.Max(peak, bars[i].High);
            double a = double.IsNaN(atr[i]) ? atr[entry] : atr[i];
            if (bars[i].Low <= peak - mult * a) return i;
        }
        return Math.Min(entry + MaxHold, bars.Count - 1);
    }

    // ── Trade construction and scoring ───────────────────────────────────────

    private static List<Trade> Build(IReadOnlyList<Ohlcv> bars, double[] atr, List<int> entries,
        Func<int, double, IReadOnlyList<Ohlcv>, double[], int> exit)
    {
        var trades = new List<Trade>();
        foreach (int e in entries)
        {
            if (e < 1 || e >= bars.Count - 2) continue;
            double r = double.IsNaN(atr[e]) || atr[e] <= 0 ? bars[e].Close * 0.02 : atr[e];
            int x = exit(e, r, bars, atr);
            if (x <= e || x >= bars.Count) continue;
            double pct = bars[x].Close / bars[e].Close - 1;
            trades.Add(new Trade(e, x, (bars[x].Close - bars[e].Close) / r, pct));
        }
        return trades;
    }

    /// <summary>
    /// Compounds the trades as a single book. Trades from the fixed entry rule do not overlap, so
    /// sequential compounding is the correct model of actually trading it.
    /// </summary>
    private static Result Score(string name, IReadOnlyList<Ohlcv> bars, List<Trade> trades)
    {
        double eq = 1, peak = 1, dd = 0;
        foreach (var t in trades.OrderBy(t => t.Entry))
        {
            eq *= 1 + t.Pct;
            peak = Math.Max(peak, eq);
            dd = Math.Max(dd, 1 - eq / peak);
        }
        return new Result(name, eq, dd, trades.Average(t => t.R),
            trades.Count(t => t.Pct > 0) / (double)trades.Count,
            trades.Average(t => t.Exit - t.Entry), trades.Count);
    }

    /// <summary>
    /// THE CONTROL. Re-runs the same entries with a random holding period drawn from the same
    /// distribution the tested rule produced, so bars-in-trade is matched and only the timing is
    /// randomised. An exit rule that cannot beat this is not exiting skilfully — it is just holding
    /// for a length of time that happens to suit the asset.
    /// </summary>
    private static double RandomExitEquity(IReadOnlyList<Ohlcv> bars, double[] atr, List<int> entries,
        List<Trade> trades, int permutations, out double p)
    {
        var holds = trades.Select(t => t.Exit - t.Entry).ToArray();
        var rng = new Random(5150);
        int runs = Math.Min(permutations, 2000);
        double acc = 0;
        int beat = 0;
        double actual = trades.OrderBy(t => t.Entry).Aggregate(1.0, (e, t) => e * (1 + t.Pct));

        for (int r = 0; r < runs; r++)
        {
            double eq = 1;
            foreach (int e in entries)
            {
                int hold = holds[rng.Next(holds.Length)];
                int x = Math.Min(e + Math.Max(1, hold), bars.Count - 1);
                if (x <= e || bars[e].Close <= 0) continue;
                eq *= bars[x].Close / bars[e].Close;
            }
            acc += eq;
            if (eq >= actual) beat++;
        }
        p = (beat + 1.0) / (runs + 1);
        return acc / runs;
    }

    /// <summary>
    /// Cody's proposed rule, tested as stated: risk-managed entry, take the bulk off at a fixed
    /// percentage gain, let the remainder run on a trailing stop. Percent rather than R, because
    /// that is how it was specified.
    /// </summary>
    private static void PartialScaleOut(IReadOnlyList<Ohlcv> bars, double[] atr, List<int> entries, int permutations)
    {
        Console.WriteLine("  ── partial scale-out: take X% off at +Y%, trail the rest ──");
        Console.WriteLine($"    {"take",6} {"at gain",8} {"trail",7} {"equity",9} {"maxDD",7} {"vs hold-to-signal",18}");

        double baseline = Score("s", bars, Build(bars, atr, entries, (e, r, b, a) => SignalExit(e, b))).Equity;

        foreach (double takeFrac in new[] { 0.5, 0.8 })
            foreach (double atGain in new[] { 0.10, 0.20 })
                foreach (double trailAtr in new[] { 3.0, 5.0 })
                {
                    double eq = 1, peak = 1, dd = 0;
                    foreach (int e in entries)
                    {
                        if (e < 1 || e >= bars.Count - 2) continue;
                        double entryPx = bars[e].Close;
                        bool tookPartial = false;
                        double realised = 0, remaining = 1.0;
                        double hwm = entryPx;

                        for (int i = e + 1; i < bars.Count && i < e + MaxHold; i++)
                        {
                            hwm = Math.Max(hwm, bars[i].High);
                            double a = double.IsNaN(atr[i]) ? atr[e] : atr[i];

                            if (!tookPartial && bars[i].High >= entryPx * (1 + atGain))
                            {
                                realised += takeFrac * atGain;      // banked at the target
                                remaining = 1 - takeFrac;
                                tookPartial = true;
                            }
                            bool trailHit = tookPartial && bars[i].Low <= hwm - trailAtr * a;
                            bool signalOut = !tookPartial && SignalExitAt(i, bars);
                            if (trailHit || signalOut || i == e + MaxHold - 1)
                            {
                                realised += remaining * (bars[i].Close / entryPx - 1);
                                break;
                            }
                        }
                        eq *= 1 + realised;
                        peak = Math.Max(peak, eq);
                        dd = Math.Max(dd, 1 - eq / peak);
                    }
                    Console.WriteLine($"    {takeFrac,6:P0} {atGain,8:P0} {trailAtr,7:0.0} {eq,9:0.00}× {dd,7:P0} " +
                                      $"{eq / Math.Max(baseline, 1e-9),18:0.00}×");
                }
        Console.WriteLine($"    (hold-to-signal baseline: {baseline:0.00}×)");
        Console.WriteLine();
    }

    private static bool SignalExitAt(int i, IReadOnlyList<Ohlcv> bars)
    {
        var z = TradingCrossCommand.ZScore(bars, ZWindow);
        return !double.IsNaN(z[i]) && z[i] < ExitZ;
    }

    private static void Verdict(List<Result> results, IReadOnlyList<Ohlcv> bars, List<int> entries)
    {
        if (results.Count == 0) return;
        Console.WriteLine("  ── VERDICT ──");
        var best = results.OrderByDescending(r => r.Equity).First();
        var sig = results.FirstOrDefault(r => r.Name.StartsWith("signal"));
        Console.WriteLine($"    best exit: {best.Name} at {best.Equity:0.00}× (maxDD {best.MaxDd:P0})");
        if (sig != null)
            Console.WriteLine($"    the entry rule's own exit: {sig.Equity:0.00}× (maxDD {sig.MaxDd:P0})");
        Console.WriteLine();
        Console.WriteLine("    Read the 'vs RANDOM exit' column, not the equity column. A rule that beats other");
        Console.WriteLine("    rules but not a random exit of the same holding length has no exit skill — it has");
        Console.WriteLine("    simply found a holding period that suits this asset, which is an exposure fact.");
    }
}
