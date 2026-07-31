using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// FOMC event study — the first test in this lab that uses REAL event dates.
///
/// <para>
/// 224 scheduled FOMC decision dates 2000–2027, parsed from the Federal Reserve's own calendar and
/// historical pages (<c>strategy-lab-data/events_fomc.json</c>). Eight per year in every year except
/// 2020, which correctly has seven — the March 2020 meeting was cancelled and replaced by the
/// emergency 15 March action. Unscheduled meetings and conference calls are kept separately.
/// </para>
///
/// <para>
/// WHY EVENTS ARE A DIFFERENT FAMILY. Every signal this lab has tested is derived from market data.
/// An event date is exogenous: it was set months in advance, it is public, and there is no
/// information asymmetry about WHEN. That removes the usual worry that the signal is a repackaged
/// price transform — a calendar date cannot be.
/// </para>
///
/// <para>
/// THE HEADLINE HYPOTHESIS is the FOMC pre-announcement drift (Lucca &amp; Moench, 2015): equity
/// returns are abnormally high in the window immediately BEFORE the announcement, and the effect is
/// large enough to account for a substantial share of the equity premium. With daily bars the
/// intraday 2pm-to-2pm window cannot be isolated, so what is measured here is the day-by-day
/// profile from t−5 to t+5, which brackets it.
/// </para>
///
/// <para>
/// THE CONTROL THAT MATTERS. FOMC decisions land overwhelmingly on Tuesdays and Wednesdays, and this
/// lab has already measured a weekday effect in SPY. A naive "event days vs all days" comparison
/// would therefore partly measure the day of the week. The random-date control here samples
/// <b>from the same weekday distribution</b> as the real event dates, so weekday is held fixed and
/// only the FOMC-ness varies.
/// </para>
/// </summary>
public static class FomcCommand
{
    private sealed class EventRow { public string date { get; set; } = ""; public bool scheduled { get; set; } }

    private const int RandomDraws = 2000;

    public static int Run(string snapshotDir, string tf, int permutations)
    {
        var path = Path.Combine(snapshotDir, "events_fomc.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"No {path}. Fetch the Fed calendar first — this command will not run on invented dates.");
            return 1;
        }

        var rows = JsonConvert.DeserializeObject<List<EventRow>>(File.ReadAllText(path)) ?? new();
        var scheduled = rows.Where(r => r.scheduled).Select(r => DateTime.Parse(r.date)).OrderBy(d => d).ToHashSet();
        var unscheduled = rows.Where(r => !r.scheduled).Select(r => DateTime.Parse(r.date)).ToHashSet();

        Console.WriteLine();
        Console.WriteLine($"===== FOMC EVENT STUDY — {scheduled.Count} scheduled decisions, {unscheduled.Count} unscheduled =====");
        Console.WriteLine($"Range {scheduled.Min():yyyy-MM-dd} → {scheduled.Max():yyyy-MM-dd}. Source: federalreserve.gov.");
        Console.WriteLine("Random control samples the SAME weekday distribution as the real dates.");
        Console.WriteLine();

        var targets = new (string Pat, string Label)[]
        {
            ("yahoo_SPY", "SPY"), ("yahoo_QQQ", "QQQ"), ("yahoo_IWM", "IWM"),
            ("yahoo_TLT", "TLT"), ("yahoo_GLD", "GLD"), ("yahoo_XLF", "XLF"),
            ("bitstamp_BTC_USDT", "BTC"), ("bitstamp_ETH_USDT", "ETH"),
        };

        var summary = new List<(string Sym, double Day0, double P0, double Pre, double PPre)>();

        foreach (var (pat, label) in targets)
        {
            var f = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
                .Where(x => !Path.GetFileName(x).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x => Path.GetFileName(x).Contains(pat, StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;

            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(f); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 500) continue;

            var r = Analyse(label, bars, scheduled, unscheduled, permutations);
            if (r != null) summary.Add(r.Value);
        }

        Verdict(summary);
        Strategy(snapshotDir, tf, scheduled, permutations);
        return 0;
    }

    /// <summary>
    /// Converts the conditional finding into an actual RULE and puts it against the control that
    /// killed MVRV: an exposure-matched timing null. "Days with property X returned more" is an
    /// exposure statement; "buying only on those days beats randomly buying the same number of
    /// days" is a timing statement. Only the second is an edge.
    ///
    /// <para>
    /// The null here is stricter than usual: the random days are drawn from the SAME weekday
    /// distribution, so it is not enough for FOMC days to be Wednesdays.
    /// </para>
    /// </summary>
    private static void Strategy(string dir, string tf, HashSet<DateTime> scheduled, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine("  ══════ AS A RULE: long only on FOMC decision days ══════");
        Console.WriteLine($"    {"asset",6} {"days in",8} {"strategy",10} {"hold",10} {"per-day",9} {"vs hold/day",12} {"net 3bps",10} {"p (exp-matched)",16}");

        foreach (var (pat, label) in new[] { ("yahoo_SPY","SPY"), ("yahoo_QQQ","QQQ"), ("yahoo_IWM","IWM"), ("yahoo_XLF","XLF") })
        {
            var f = Directory.GetFiles(dir, $"*_{tf}.json")
                .Where(x => !Path.GetFileName(x).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x => Path.GetFileName(x).Contains(pat, StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var bars = SnapshotCommand.Load(f).Bars;
            if (bars.Count < 500) continue;

            var rets = new double[bars.Count];
            for (int i = 1; i < bars.Count; i++)
                rets[i] = bars[i].Close > 0 && bars[i-1].Close > 0 ? Math.Log(bars[i].Close / bars[i-1].Close) : 0;

            var idxOf = new Dictionary<DateTime,int>();
            for (int i = 0; i < bars.Count; i++) idxOf[bars[i].Date.Date] = i;
            int Locate(DateTime d) { for (int k=0;k<6;k++) if (idxOf.TryGetValue(d.Date.AddDays(k), out int i)) return i; return -1; }

            var ev = scheduled.Select(Locate).Where(i => i > 1 && i < bars.Count).ToList();
            if (ev.Count < 30) continue;

            double logSum = ev.Sum(i => rets[i]);
            double strat = Math.Exp(logSum);
            double hold  = Math.Exp(rets.Skip(1).Sum());
            // Entering at the prior close and exiting at this close is one round trip per event.
            double net   = Math.Exp(logSum - ev.Count * 2 * 3.0 / 10000.0);

            // Exposure-matched, weekday-matched null.
            var wd = ev.GroupBy(i => bars[i].Date.DayOfWeek).ToDictionary(g => g.Key, g => g.Count());
            var pool = Enumerable.Range(1, bars.Count-1).GroupBy(i => bars[i].Date.DayOfWeek)
                                 .ToDictionary(g => g.Key, g => g.ToList());
            var rng = new Random(1234);
            int runs = Math.Min(permutations, 2000), beat = 0;
            for (int r = 0; r < runs; r++)
            {
                double acc = 0;
                foreach (var (day, n) in wd)
                {
                    if (!pool.TryGetValue(day, out var p2) || p2.Count == 0) continue;
                    for (int k = 0; k < n; k++) acc += rets[p2[rng.Next(p2.Count)]];
                }
                if (Math.Exp(acc) >= strat) beat++;
            }
            double pv = (beat + 1.0) / (runs + 1);

            Console.WriteLine($"    {label,6} {ev.Count,8} {strat,10:0.00}× {hold,10:0.00}× " +
                              $"{Math.Exp(logSum/ev.Count)-1,9:+0.000%;-0.000%;0} " +
                              $"{(Math.Exp(logSum/ev.Count)-1)/(Math.Exp(rets.Skip(1).Sum()/(bars.Count-1))-1),12:0.0}× " +
                              $"{net,10:0.00}× {pv,16:0.0000}{(pv<=0.05?" *":"")}" );
        }
        Console.WriteLine("    'vs hold/day' = per-day return on decision days divided by the average day.");
        Console.WriteLine();
        Decay(dir, tf, scheduled);
        Console.WriteLine();
        Console.WriteLine("    Costs at 3 bps/side round trip — SPY-class spreads. One round trip per event.");
        Console.WriteLine("    The strategy is in the market ~8 days a year, so its TOTAL return is small by");
        Console.WriteLine("    construction; the question is whether the per-day rate beats matched random days.");
        Console.WriteLine();
    }

    /// <summary>
    /// Has the effect decayed since it was published?
    ///
    /// <para>
    /// Lucca &amp; Moench documented the pre-FOMC drift in the Journal of Finance in 2015. A
    /// documented anomaly is a traded anomaly, and Narang's test for a dead edge is that the move
    /// arrives faster because someone is doing it before you. The cheap version of that check is a
    /// straight before/after split on the publication date — if the effect is intact post-2015 it is
    /// unusual, and if it has halved that is exactly what the literature would predict.
    /// </para>
    /// </summary>
    private static void Decay(string dir, string tf, HashSet<DateTime> scheduled)
    {
        Console.WriteLine("  ── has it decayed since Lucca & Moench published it in 2015? ──");
        Console.WriteLine($"    {"asset",6} {"pre-2015 /day",15} {"n",5} {"post-2015 /day",16} {"n",5} {"ratio",8}");

        foreach (var (pat, label) in new[] { ("yahoo_SPY","SPY"), ("yahoo_QQQ","QQQ"), ("yahoo_IWM","IWM"), ("yahoo_XLF","XLF") })
        {
            var f = Directory.GetFiles(dir, $"*_{tf}.json")
                .Where(x => !Path.GetFileName(x).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x => Path.GetFileName(x).Contains(pat, StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;
            var bars = SnapshotCommand.Load(f).Bars;
            var rets = new double[bars.Count];
            for (int i = 1; i < bars.Count; i++)
                rets[i] = bars[i].Close > 0 && bars[i-1].Close > 0 ? Math.Log(bars[i].Close / bars[i-1].Close) : 0;
            var idxOf = new Dictionary<DateTime,int>();
            for (int i = 0; i < bars.Count; i++) idxOf[bars[i].Date.Date] = i;
            int Locate(DateTime d) { for (int k=0;k<6;k++) if (idxOf.TryGetValue(d.Date.AddDays(k), out int i)) return i; return -1; }

            var pre  = scheduled.Where(d => d.Year <  2015).Select(Locate).Where(i => i > 1 && i < bars.Count).ToList();
            var post = scheduled.Where(d => d.Year >= 2015).Select(Locate).Where(i => i > 1 && i < bars.Count).ToList();
            if (pre.Count < 20 || post.Count < 20) continue;

            double a = pre.Average(i => rets[i]), b = post.Average(i => rets[i]);
            Console.WriteLine($"    {label,6} {a,15:+0.000%;-0.000%;0} {pre.Count,5} {b,16:+0.000%;-0.000%;0} {post.Count,5} {(a != 0 ? b/a : 0),8:0.00}×");
        }
        Console.WriteLine("    A ratio near 1 means intact; near 0 means arbitraged away since publication.");
    }

    private static (string, double, double, double, double)? Analyse(string label, List<Ohlcv> bars,
        HashSet<DateTime> scheduled, HashSet<DateTime> unscheduled, int permutations)
    {
        // Daily returns indexed by bar position, plus a date→index map so an event date that falls
        // on a holiday or weekend maps to the NEXT trading bar rather than being dropped.
        var rets = new double[bars.Count];
        for (int i = 1; i < bars.Count; i++)
            rets[i] = bars[i].Close > 0 && bars[i - 1].Close > 0 ? Math.Log(bars[i].Close / bars[i - 1].Close) : 0;

        var idxOf = new Dictionary<DateTime, int>();
        for (int i = 0; i < bars.Count; i++) idxOf[bars[i].Date.Date] = i;

        int Locate(DateTime d)
        {
            for (int k = 0; k < 6; k++)
                if (idxOf.TryGetValue(d.Date.AddDays(k), out int i)) return i;
            return -1;
        }

        var evIdx = scheduled.Select(Locate).Where(i => i > 10 && i < bars.Count - 10).ToList();
        if (evIdx.Count < 30) return null;

        Console.WriteLine($"  ══════ {label} ({bars.Count:N0} bars, {bars[0].Date:yyyy-MM} → {bars[^1].Date:yyyy-MM}, {evIdx.Count} events matched) ══════");

        // Weekday distribution of the matched event bars — the control has to reproduce it.
        var wdCount = evIdx.GroupBy(i => bars[i].Date.DayOfWeek).ToDictionary(g => g.Key, g => g.Count());
        var byWeekday = Enumerable.Range(0, bars.Count).Where(i => i > 10 && i < bars.Count - 10)
            .GroupBy(i => bars[i].Date.DayOfWeek).ToDictionary(g => g.Key, g => g.ToList());

        double allDays = rets.Skip(1).Average();
        Console.WriteLine($"    baseline: all days {allDays:+0.000%;-0.000%;0}/day   " +
                          $"event weekdays: {string.Join(", ", wdCount.OrderByDescending(k => k.Value).Select(k => $"{k.Key.ToString()[..3]} {k.Value}"))}");

        // Day-by-day profile with a weekday-matched random control at each offset.
        Console.WriteLine($"    {"offset",7} {"mean",10} {"random",10} {"excess",10} {"p",8} {"win%",6}");
        double day0 = 0, p0 = 1, pre = 0, ppre = 1;

        foreach (int off in new[] { -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5 })
        {
            var vals = evIdx.Where(i => i + off >= 1 && i + off < bars.Count).Select(i => rets[i + off]).ToList();
            if (vals.Count < 30) continue;
            double mean = vals.Average();
            var (rndMean, p) = RandomWeekdayControl(rets, bars, evIdx, wdCount, byWeekday, off, permutations);
            double win = vals.Count(v => v > 0) / (double)vals.Count;

            Console.WriteLine($"    {off,7} {mean,10:+0.000%;-0.000%;0} {rndMean,10:+0.000%;-0.000%;0} " +
                              $"{mean - rndMean,10:+0.000%;-0.000%;0} {p,8:0.0000}{(p <= 0.05 ? " *" : "  ")} {win,6:P0}");

            if (off == 0) { day0 = mean - rndMean; p0 = p; }
            if (off == -1) { pre = mean - rndMean; ppre = p; }
        }

        // Cumulative windows, since a drift can be spread across days no single one of which clears.
        foreach (var (a, b, name) in new[] { (-5, -1, "t−5…t−1 (pre)"), (0, 0, "t (decision)"), (1, 5, "t+1…t+5 (post)") })
        {
            var vals = evIdx.Select(i =>
            {
                double s = 0;
                for (int k = a; k <= b; k++) if (i + k >= 1 && i + k < bars.Count) s += rets[i + k];
                return s;
            }).ToList();
            Console.WriteLine($"    {name,-16} cumulative {vals.Average(),8:+0.00%;-0.00%;0}   " +
                              $"({vals.Count(v => v > 0) / (double)vals.Count:P0} positive)");
        }

        // Unscheduled meetings are a different animal — they happen BECAUSE something is wrong.
        var unIdx = unscheduled.Select(Locate).Where(i => i > 10 && i < bars.Count - 10).ToList();
        if (unIdx.Count >= 8)
            Console.WriteLine($"    unscheduled (n={unIdx.Count}): day 0 {unIdx.Select(i => rets[i]).Average():+0.00%;-0.00%;0}   " +
                              "— crisis meetings, reported for contrast not as a signal");
        Console.WriteLine();

        return (label, day0, p0, pre, ppre);
    }

    /// <summary>
    /// Draws a control set with the SAME number of dates on the SAME weekdays as the real events,
    /// then measures the same offset. Without the weekday match this would partly be measuring that
    /// FOMC decisions land on Tuesdays and Wednesdays, which is a calendar fact rather than a
    /// monetary-policy one.
    /// </summary>
    private static (double Mean, double P) RandomWeekdayControl(double[] rets, List<Ohlcv> bars,
        List<int> evIdx, Dictionary<DayOfWeek, int> wdCount, Dictionary<DayOfWeek, List<int>> byWeekday,
        int off, int permutations)
    {
        double observed = evIdx.Where(i => i + off >= 1 && i + off < bars.Count).Select(i => rets[i + off]).Average();
        var rng = new Random(2718 + off);
        int runs = Math.Min(permutations, RandomDraws);
        int atLeast = 0;
        double acc = 0;

        for (int r = 0; r < runs; r++)
        {
            double sum = 0; int n = 0;
            foreach (var (wd, count) in wdCount)
            {
                if (!byWeekday.TryGetValue(wd, out var pool) || pool.Count == 0) continue;
                for (int k = 0; k < count; k++)
                {
                    int i = pool[rng.Next(pool.Count)];
                    if (i + off >= 1 && i + off < bars.Count) { sum += rets[i + off]; n++; }
                }
            }
            if (n == 0) continue;
            double m = sum / n;
            acc += m;
            if (Math.Abs(m) >= Math.Abs(observed)) atLeast++;
        }

        return (acc / runs, (atLeast + 1.0) / (runs + 1));
    }

    private static void Verdict(List<(string Sym, double Day0, double P0, double Pre, double PPre)> s)
    {
        if (s.Count == 0) { Console.WriteLine("  No assets analysed."); return; }

        Console.WriteLine("  ── VERDICT ──");
        Console.WriteLine($"    {"asset",7} {"day 0 excess",14} {"p",8}   {"t−1 excess",12} {"p",8}");
        foreach (var r in s)
            Console.WriteLine($"    {r.Sym,7} {r.Day0,14:+0.000%;-0.000%;0} {r.P0,8:0.0000}{(r.P0 <= 0.05 ? " *" : "  ")} " +
                              $"{r.Pre,12:+0.000%;-0.000%;0} {r.PPre,8:0.0000}{(r.PPre <= 0.05 ? " *" : "")}");

        int d0 = s.Count(r => r.P0 <= 0.05), pre = s.Count(r => r.PPre <= 0.05);
        Console.WriteLine();
        Console.WriteLine($"    Significant at p≤0.05: decision day {d0}/{s.Count}, day before {pre}/{s.Count}.");
        Console.WriteLine($"    {s.Count} assets × 11 offsets = {s.Count * 11} tests; expect ~{s.Count * 11 * 0.05:0.0} false positives");
        Console.WriteLine($"    at α=0.05. A Bonferroni threshold would be ~{0.05 / (s.Count * 11):0.0000}.");
        Console.WriteLine("    An effect that shows up on the SAME offset across several independent assets is");
        Console.WriteLine("    worth more than one asset clearing a threshold, because equities, bonds, gold and");
        Console.WriteLine("    crypto do not share a sampling error.");
    }
}
