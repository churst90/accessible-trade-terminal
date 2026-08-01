using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Is a fixed 1:3 target the best target — and are losses streaky?
///
/// <para>
/// TWO CLAIMS FROM ONE TRADE SERIES. First: "I tried my strategy with 1:1, 1:2, 1:8 over hundreds of
/// trades … 1:3 had the best, not the win rate, but best profit when accumulated." Second: "if I have
/// two losses in a row I'm done for the day, and if I have four losses in a row I'm done for the
/// week." Both need the same thing — a fixed entry producing many trades — so they are measured
/// together.
/// </para>
///
/// <para>
/// THE QUESTION UNDERNEATH THE TARGET CLAIM is not "1:3 or not". This lab has already measured that
/// fixed-percentage scale-outs destroy 95–100% of the return in crypto, because the R-distribution
/// has a fat right tail and capping it removes the only trades that pay. So the real question is
/// whether that tail exists in every asset class. Measure the distribution first and the target
/// choice answers itself — which is why the top-decile share is printed before any target sweep.
/// </para>
///
/// <para>
/// THE STREAK CLAIM HAS A PRIOR TEST. If trade outcomes are serially independent, a stop-after-two-
/// losses rule cannot change expectancy at all — only variance. So the autocorrelation of the
/// win/loss sequence is measured first, against a shuffle control. If it is zero the rule is
/// behavioural, which is a good reason to keep it and a dishonest reason to claim an edge from it.
/// </para>
///
/// <para>
/// The entry is held fixed everywhere: the z-score trend cross this lab has used for every exit
/// study, so results are comparable with EXIT_FINDINGS. Risk is 1 ATR(14) at entry; R is measured
/// against that.
/// </para>
/// </summary>
public static class TargetsCommand
{
    private const int ZWindow = 50;
    private const double EntryZ = 1.0;
    private const double ExitZ = 0.5;
    private const int MaxHold = 400;

    /// <param name="R">Outcome if the trade is simply held to the signal exit.</param>
    /// <param name="MaxFav">Best R the trade ever reached BEFORE the stop was hit. Without this the
    /// target sweep is unfair to every target: a trade that ran to +3R and closed at -0.5R would be
    /// scored a loss when a 1:3 target would have banked it.</param>
    /// <param name="StoppedFirst">True if -1R was reached before <see cref="MaxFav"/>.</param>
    private sealed record Trade(double R, int Bars, double MaxFav, bool StoppedFirst);

    public static int Run(string snapshotDir, string tf)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var byClass = new Dictionary<string, List<Trade>>(StringComparer.OrdinalIgnoreCase);
        var streakByClass = new Dictionary<string, List<List<bool>>>(StringComparer.OrdinalIgnoreCase);
        int instruments = 0;

        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            if (snap.Bars.Count < 600) continue;
            var bars = snap.Bars.ToArray();
            string cls = Path.GetFileName(f).StartsWith("bitstamp") || Path.GetFileName(f).StartsWith("mexc")
                ? "crypto" : "equities";

            var trades = SignalExitTrades(bars);
            if (trades.Count < 20) continue;

            if (!byClass.TryGetValue(cls, out var list)) byClass[cls] = list = new();
            list.AddRange(trades);

            if (!streakByClass.TryGetValue(cls, out var st)) streakByClass[cls] = st = new();
            st.Add(trades.Select(t => t.R > 0).ToList());
            instruments++;
        }

        if (byClass.Count == 0) { Console.Error.WriteLine("No instrument produced enough trades."); return 1; }

        Console.WriteLine();
        Console.WriteLine("═════ TARGETS AND STREAKS ═════");
        Console.WriteLine($"{instruments} instruments · {tf} · fixed entry (z{ZWindow} crosses +{EntryZ}), risk = 1 ATR(14)");
        Console.WriteLine();

        foreach (var (cls, trades) in byClass.OrderBy(k => k.Key))
        {
            Console.WriteLine($"── {cls.ToUpperInvariant()} — {trades.Count:N0} trades " + new string('─', 40));
            TailShape(trades);
            TargetSweep(trades);
            Streaks(streakByClass[cls], cls);
            Console.WriteLine();
        }
        return 0;
    }

    // ── The fixed entry, and the unmanaged trade it produces ────────────────────

    private static List<Trade> SignalExitTrades(Ohlcv[] bars)
    {
        var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars, 14);
        var z = ZScores(bars);
        var trades = new List<Trade>();

        for (int i = ZWindow + 1; i < bars.Length - 1; i++)
        {
            if (!(z[i - 1] <= EntryZ && z[i] > EntryZ)) continue;
            double risk = atr[i];
            if (double.IsNaN(risk) || risk <= 0) continue;

            double entry = bars[i].Close;
            int exit = Math.Min(i + MaxHold, bars.Length - 1);
            for (int k = i + 1; k <= exit; k++)
                if (z[k] < ExitZ) { exit = k; break; }

            // Walk the path so a target sweep can ask "did it EVER reach +kR, and did the stop come
            // first?" Highs and lows are used, not closes, because a target is an order resting in
            // the book — and the stop is checked first within each bar, which is the pessimistic
            // assumption when both are touched in the same bar.
            double maxFav = 0; bool stoppedFirst = false;
            for (int k = i + 1; k <= exit; k++)
            {
                if ((bars[k].Low - entry) / risk <= -1.0) { stoppedFirst = true; break; }
                maxFav = Math.Max(maxFav, (bars[k].High - entry) / risk);
            }

            trades.Add(new Trade((bars[exit].Close - entry) / risk, exit - i, maxFav, stoppedFirst));
            i = exit;                       // no overlapping positions
        }
        return trades;
    }

    private static double[] ZScores(Ohlcv[] bars)
    {
        var z = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            if (i < ZWindow) { z[i] = double.NaN; continue; }
            double mean = 0;
            for (int k = i - ZWindow + 1; k <= i; k++) mean += bars[k].Close;
            mean /= ZWindow;
            double var = 0;
            for (int k = i - ZWindow + 1; k <= i; k++) var += Math.Pow(bars[k].Close - mean, 2);
            double sd = Math.Sqrt(var / ZWindow);
            z[i] = sd <= 0 ? 0 : (bars[i].Close - mean) / sd;
        }
        return z;
    }

    // ── 1. The shape of the distribution, before any target is chosen ───────────

    private static void TailShape(List<Trade> t)
    {
        var sorted = t.Select(x => x.R).OrderByDescending(x => x).ToList();
        double total = sorted.Sum();
        int top10 = Math.Max(1, sorted.Count / 10);
        int top1 = Math.Max(1, sorted.Count / 100);
        double shareTop10 = total == 0 ? double.NaN : sorted.Take(top10).Sum() / total;
        double shareTop1 = total == 0 ? double.NaN : sorted.Take(top1).Sum() / total;
        double win = t.Count(x => x.R > 0) / (double)t.Count;

        Console.WriteLine("  shape of the R-distribution (this decides the target question):");
        Console.WriteLine($"    win rate {win * 100:0.0}%   mean {t.Average(x => x.R):+0.000;-0.000}R   median {Median(t.Select(x => x.R).ToList()):+0.000;-0.000}R");
        Console.WriteLine($"    best trade {sorted[0]:0.0}R   worst {sorted[^1]:0.0}R   avg hold {t.Average(x => x.Bars):0} bars");
        Console.WriteLine($"    TOP 10% OF TRADES CARRY {shareTop10 * 100:0}% OF THE TOTAL R   ·   top 1% carry {shareTop1 * 100:0}%");
        Console.WriteLine();
    }

    // ── 2. The target sweep ─────────────────────────────────────────────────────

    private static void TargetSweep(List<Trade> t)
    {
        Console.WriteLine("  fixed target vs letting the signal decide:");
        Console.WriteLine($"    {"target",-12}{"total R",10}{"per trade",11}{"win rate",10}");

        foreach (double mult in new[] { 1.0, 2.0, 3.0, 4.0, 6.0, 8.0 })
        {
            // A trade that reached the target takes exactly +mult R; one that did not takes its
            // actual outcome, floored at -1R because the stop would have been hit first.
            double totalR = 0; int wins = 0;
            foreach (var x in t)
            {
                // Reached the target before being stopped → the target is banked. Stopped first →
                // -1R. Neither → the trade closes where the signal closed it.
                double r = !x.StoppedFirst && x.MaxFav >= mult ? mult
                         : x.StoppedFirst ? -1.0
                         : Math.Max(x.R, -1.0);
                totalR += r;
                if (r > 0) wins++;
            }
            Console.WriteLine($"    {"1:" + mult,-12}{totalR,10:0.0}{totalR / t.Count,11:+0.000;-0.000}{wins / (double)t.Count * 100,9:0.0}%");
        }

        double sigTotal = t.Sum(x => x.StoppedFirst ? -1.0 : Math.Max(x.R, -1.0));
        Console.WriteLine($"    {"signal exit",-12}{sigTotal,10:0.0}{sigTotal / t.Count,11:+0.000;-0.000}"
                        + $"{t.Count(x => !x.StoppedFirst && x.R > 0) / (double)t.Count * 100,9:0.0}%");
        Console.WriteLine("    (target hit is path-dependent: reached +kR before -1R. Stop checked first within a bar.)");
        Console.WriteLine();
    }

    // ── 3. Are losses streaky? ──────────────────────────────────────────────────

    private static void Streaks(List<List<bool>> perInstrument, string cls)
    {
        // Lag-1 autocorrelation of the win/loss sequence, pooled per instrument then averaged, with
        // a shuffle control: if outcomes are independent, a stop-after-N-losses rule cannot move
        // expectancy at all.
        double observed = perInstrument.Where(s => s.Count >= 20).Average(Lag1);
        var rng = new Random(20260801);
        int atLeast = 0; const int perms = 2000;
        for (int p = 0; p < perms; p++)
        {
            double sum = 0; int n = 0;
            foreach (var s in perInstrument.Where(s => s.Count >= 20))
            {
                var shuffled = s.OrderBy(_ => rng.Next()).ToList();
                sum += Lag1(shuffled); n++;
            }
            if (sum / n >= observed) atLeast++;
        }

        Console.WriteLine("  are losses streaky? (if outcomes are independent, a stop-after-N-losses rule");
        Console.WriteLine("  cannot change expectancy — only variance)");
        Console.WriteLine($"    lag-1 autocorrelation of win/loss: {observed:+0.000;-0.000}   p vs shuffle {(atLeast + 1.0) / (perms + 1.0):0.000}");
    }

    private static double Lag1(List<bool> s)
    {
        var x = s.Select(b => b ? 1.0 : 0.0).ToList();
        double mean = x.Average();
        double num = 0, den = 0;
        for (int i = 1; i < x.Count; i++) num += (x[i] - mean) * (x[i - 1] - mean);
        foreach (var v in x) den += (v - mean) * (v - mean);
        return den <= 0 ? 0 : num / den;
    }

    private static double Median(List<double> v)
    {
        v.Sort();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
