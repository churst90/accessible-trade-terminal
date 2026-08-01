using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Should position size scale with how many timeframes agree?
///
/// <para>
/// THE CLAIM: "if everything is in structure, everything is bullish, beautiful zones, I'm going to
/// risk more; if there's things that doesn't tick the box I'm shaving off my risk" — risking 1% to
/// 6% by how much of the picture agrees. It is the only claim in the queue about SIZING, which is
/// the corner of the strategy / risk-management / trade-management triangle this lab has never
/// measured: twenty-odd recorded edges, all of them about entries or exits.
/// </para>
///
/// <para>
/// THE TEST is a conditional-expectancy test. Hold the entry fixed, bucket its trades by how many
/// higher-timeframe conditions agreed at the moment of entry, and compare per-trade R. If expectancy
/// rises with agreement, sizing by agreement is justified arithmetic; if it is flat, the 1%-to-6%
/// range is a feeling.
/// </para>
///
/// <para>
/// THE TRAP THIS HAS TO CLEAR, and it is a serious one: the entry and the agreement conditions are
/// all computed from the same price series, so agreement is partly fixed by arithmetic rather than
/// observed. A z-score cross upward mechanically implies price is above its own recent mean, which
/// makes "above the 50-day" almost automatic. The overlap is therefore MEASURED and printed first —
/// the base rate of each condition at entry bars against its base rate at all bars. A condition that
/// is true at 95% of entries carries no information about those entries whatever the R column says.
/// </para>
/// </summary>
public static class MtfSizingCommand
{
    private const int ZWindow = 50;
    private const double EntryZ = 1.0;
    private const double ExitZ = 0.5;
    private const int MaxHold = 400;

    private sealed record Trade(double R, int Agree, bool Weekly, bool Mid, bool Long);

    public static int Run(string snapshotDir, string tf)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var byClass = new Dictionary<string, List<Trade>>(StringComparer.OrdinalIgnoreCase);
        var baseRates = new Dictionary<string, (double W, double M, double L, int N)>(StringComparer.OrdinalIgnoreCase);
        int instruments = 0;

        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            if (snap.Bars.Count < 600) continue;
            var bars = snap.Bars.ToArray();
            string cls = Path.GetFileName(f).StartsWith("bitstamp") || Path.GetFileName(f).StartsWith("mexc")
                ? "crypto" : "equities";

            var (trades, allBarRates) = Collect(bars);
            if (trades.Count < 20) continue;

            if (!byClass.TryGetValue(cls, out var l)) byClass[cls] = l = new();
            l.AddRange(trades);

            var prev = baseRates.TryGetValue(cls, out var p) ? p : (0, 0, 0, 0);
            baseRates[cls] = (prev.Item1 + allBarRates.W, prev.Item2 + allBarRates.M,
                              prev.Item3 + allBarRates.L, prev.Item4 + 1);
            instruments++;
        }

        if (byClass.Count == 0) { Console.Error.WriteLine("No instrument produced enough trades."); return 1; }

        Console.WriteLine();
        Console.WriteLine("═════ DOES MULTI-TIMEFRAME AGREEMENT JUSTIFY SIZING UP? ═════");
        Console.WriteLine($"{instruments} instruments · {tf} · fixed entry (z{ZWindow} crosses +{EntryZ}), risk = 1 ATR(14)");
        Console.WriteLine();
        Console.WriteLine("Agreement components, all knowable at entry: last COMPLETED weekly candle bullish ·");
        Console.WriteLine("close above the 50-bar mean · close above the 200-bar mean.");
        Console.WriteLine();

        foreach (var (cls, trades) in byClass.OrderBy(k => k.Key))
        {
            var br = baseRates[cls];
            Console.WriteLine($"── {cls.ToUpperInvariant()} — {trades.Count:N0} trades " + new string('─', 38));

            // The overlap check comes FIRST, because it can invalidate the table below it.
            Console.WriteLine("  overlap check — how often each condition is true (entry bars vs all bars):");
            Console.WriteLine($"    weekly bullish   {trades.Count(t => t.Weekly) / (double)trades.Count * 100,5:0.0}%  vs  {br.W / br.N * 100,5:0.0}%");
            Console.WriteLine($"    above 50-bar     {trades.Count(t => t.Mid) / (double)trades.Count * 100,5:0.0}%  vs  {br.M / br.N * 100,5:0.0}%");
            Console.WriteLine($"    above 200-bar    {trades.Count(t => t.Long) / (double)trades.Count * 100,5:0.0}%  vs  {br.L / br.N * 100,5:0.0}%");
            Console.WriteLine();

            Console.WriteLine($"    {"agreement",-12}{"trades",8}{"per trade",12}{"win rate",10}{"total R",10}");
            for (int a = 0; a <= 3; a++)
            {
                var g = trades.Where(t => t.Agree == a).ToList();
                if (g.Count == 0) { Console.WriteLine($"    {a + " of 3",-12}{0,8}{"—",12}{"—",10}{"—",10}"); continue; }
                Console.WriteLine($"    {a + " of 3",-12}{g.Count,8}{g.Average(x => x.R),12:+0.000;-0.000}"
                                + $"{g.Count(x => x.R > 0) / (double)g.Count * 100,9:0.0}%{g.Sum(x => x.R),10:0.0}");
            }

            var lo = trades.Where(t => t.Agree <= 1).ToList();
            var hi = trades.Where(t => t.Agree == 3).ToList();
            if (lo.Count >= 10 && hi.Count >= 10)
            {
                double diff = hi.Average(x => x.R) - lo.Average(x => x.R);
                Console.WriteLine();
                Console.WriteLine($"    full agreement minus weak agreement: {diff:+0.000;-0.000} R/trade  (p {Permute(trades, hi.Count):0.000})");
            }
            Console.WriteLine();
        }

        Console.WriteLine("Reading it: a rising per-trade R across agreement buckets is the only thing that would");
        Console.WriteLine("justify sizing up — but read the overlap check first. A condition true at nearly every");
        Console.WriteLine("entry cannot be discriminating between those entries, whatever the R column shows.");
        return 0;
    }

    /// <summary>Random-subset null: does any group of this size do as well, by luck?</summary>
    private static double Permute(List<Trade> all, int size)
    {
        double observed = all.Where(t => t.Agree == 3).Average(t => t.R);
        var rng = new Random(20260801);
        int atLeast = 0; const int perms = 5000;
        for (int p = 0; p < perms; p++)
        {
            double sum = 0;
            for (int k = 0; k < size; k++) sum += all[rng.Next(all.Count)].R;
            if (sum / size >= observed) atLeast++;
        }
        return (atLeast + 1.0) / (perms + 1.0);
    }

    private static (List<Trade>, (double W, double M, double L)) Collect(Ohlcv[] bars)
    {
        var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars, 14);
        var z = ZScores(bars);
        var sma50 = Sma(bars, 50);
        var sma200 = Sma(bars, 200);
        var weeklyBull = WeeklyBias(bars);

        var trades = new List<Trade>();
        int wc = 0, mc = 0, lc = 0, all = 0;

        for (int i = 200; i < bars.Length - 1; i++)
        {
            if (!double.IsNaN(sma200[i]))
            {
                all++;
                if (weeklyBull[i]) wc++;
                if (bars[i].Close > sma50[i]) mc++;
                if (bars[i].Close > sma200[i]) lc++;
            }

            if (!(z[i - 1] <= EntryZ && z[i] > EntryZ)) continue;
            double risk = atr[i];
            if (double.IsNaN(risk) || risk <= 0 || double.IsNaN(sma200[i])) continue;

            bool w = weeklyBull[i], m = bars[i].Close > sma50[i], lg = bars[i].Close > sma200[i];

            double entry = bars[i].Close;
            int exit = Math.Min(i + MaxHold, bars.Length - 1);
            for (int k = i + 1; k <= exit; k++) if (z[k] < ExitZ) { exit = k; break; }

            trades.Add(new Trade((bars[exit].Close - entry) / risk, (w ? 1 : 0) + (m ? 1 : 0) + (lg ? 1 : 0), w, m, lg));
            i = exit;
        }

        return (trades, all == 0 ? (0, 0, 0) : (wc / (double)all, mc / (double)all, lc / (double)all));
    }

    /// <summary>
    /// Was the last COMPLETED weekly candle bullish? Completed is the operative word — using the
    /// week in progress would read a close that has not happened.
    /// </summary>
    private static bool[] WeeklyBias(Ohlcv[] bars)
    {
        var outp = new bool[bars.Length];
        bool lastCompleted = false;
        double weekOpen = bars[0].Open;
        int curWeek = System.Globalization.ISOWeek.GetWeekOfYear(bars[0].Date);

        for (int i = 0; i < bars.Length; i++)
        {
            int wk = System.Globalization.ISOWeek.GetWeekOfYear(bars[i].Date);
            if (wk != curWeek)
            {
                lastCompleted = bars[i - 1].Close > weekOpen;
                weekOpen = bars[i].Open;
                curWeek = wk;
            }
            outp[i] = lastCompleted;
        }
        return outp;
    }

    private static double[] Sma(Ohlcv[] bars, int n)
    {
        var o = new double[bars.Length];
        double sum = 0;
        for (int i = 0; i < bars.Length; i++)
        {
            sum += bars[i].Close;
            if (i >= n) sum -= bars[i - n].Close;
            o[i] = i >= n - 1 ? sum / n : double.NaN;
        }
        return o;
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
}
