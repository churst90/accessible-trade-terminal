using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Two more non-price families: POSITIONING (CFTC Commitments of Traders) and EVENTS/CALENDAR.
///
/// <para>
/// POSITIONING. The COT report is net speculator positioning as a percentage of open interest —
/// regulated, weekly, and reported rather than exchange-self-declared, which is a materially better
/// data source than the funding/open-interest composite that produced a clean null in
/// <see cref="CrowdingCommand"/>. The contrarian claim, and Jason Shapiro's whole method, is that
/// extreme net-long speculator positioning precedes falls and extreme net-short precedes rallies.
/// It runs back to 2006 for the S&amp;P, Nasdaq and gold — 20 years, far longer than any crypto series
/// here.
/// </para>
///
/// <para>
/// EVENTS. Narang's taxonomy has an entire alpha family this lab has never touched. The value of an
/// event study is that the DATE is known in advance and carries no information asymmetry — you are
/// testing the reaction, not forecasting the news. Two are testable with what is on disk without
/// inventing anything: Bitcoin halvings (four exactly-known dates) and pure calendar structure
/// (turn-of-month, day-of-week, month-of-year), which needs no external data at all.
/// </para>
///
/// <para>
/// NOT TESTED, DELIBERATELY: FOMC and CPI release dates. They are the obvious candidates and they
/// are not in the snapshot set. Reconstructing ~160 meeting dates from memory would put fabricated
/// data at the centre of the result, which is worse than not running it. That is a data-fetch job.
/// </para>
///
/// <para>
/// THE CONTROL throughout is the unconditional forward return over the same bars. Crypto and
/// equities both rose across these samples, so any subset will look profitable in isolation; only
/// the difference from the all-bars baseline belongs to the signal.
/// </para>
/// </summary>
public static class EventsCommand
{
    private const int ZWin = 26;   // weeks — the provider's own COT z-score window

    private sealed class XsFile { public List<XsPoint> Points { get; set; } = new(); }
    private sealed class XsPoint { public long Ts { get; set; } public double Value { get; set; } }

    public static int Run(string snapshotDir, string tf, int horizon, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine($"===== POSITIONING & EVENTS — forward horizon {horizon} bars =====");
        Console.WriteLine();

        Cot(snapshotDir, tf, horizon, permutations);
        Halvings(snapshotDir, tf, permutations);
        Calendar(snapshotDir, tf, horizon, permutations);
        return 0;
    }

    // ── 1. COT positioning ───────────────────────────────────────────────────

    private static void Cot(string dir, string tf, int horizon, int permutations)
    {
        Console.WriteLine("  ══════ CFTC COT — net speculator % of open interest ══════");
        Console.WriteLine("    Contrarian claim: extreme net-long precedes falls, extreme net-short precedes rallies.");
        Console.WriteLine();

        var pairs = new (string Cot, string Price, string Label)[]
        {
            ("sp500_cot", "SPY", "S&P 500"),
            ("nasdaq_cot", "QQQ", "Nasdaq"),
            ("gold_cot", "XAU", "Gold"),
            ("bitcoin_cot", "bitstamp_BTC_USDT", "Bitcoin"),
        };

        foreach (var (cotName, pricePat, label) in pairs)
        {
            var cot = LoadXs(dir, $"xs_cftc_{cotName}_1w.json");
            if (cot == null) { Console.WriteLine($"    {label,-10} no COT file"); continue; }

            var pf = Directory.GetFiles(dir, $"*_{tf}.json")
                .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(f => Path.GetFileName(f).Contains(pricePat, StringComparison.OrdinalIgnoreCase));
            if (pf == null) { Console.WriteLine($"    {label,-10} no price snapshot"); continue; }

            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(pf); } catch { continue; }
            var bars = snap.Bars;
            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);

            // COT is published Friday for the Tuesday of record — a three-day reporting lag that a
            // backtest MUST honour, or it trades on positioning nobody could yet see. Six days is
            // used, which is conservative.
            var aligned = AlignLagged(cot, bars, 6);
            var z = LabStats.RollingZ(aligned, ZWin * 5);   // 26 weeks in daily bars

            var rows = new List<(double Z, double Fwd)>();
            for (int i = 200; i < bars.Count - horizon; i++)
            {
                if (double.IsNaN(z[i]) || double.IsNaN(atr[i]) || atr[i] <= 0) continue;
                rows.Add((z[i], (bars[i + horizon].Close - bars[i].Close) / atr[i]));
            }
            if (rows.Count < 500) { Console.WriteLine($"    {label,-10} only {rows.Count} rows"); continue; }

            var sorted = rows.OrderBy(r => r.Z).ToList();
            int per = sorted.Count / 5;
            double baseline = rows.Average(r => r.Fwd);
            double gap = sorted.Take(per).Average(r => r.Fwd) - sorted.TakeLast(per).Average(r => r.Fwd);
            double p = PermutationP(rows.Select(r => r.Fwd).ToArray(), per, per, gap, permutations);

            Console.WriteLine($"    {label,-10} net-short quintile {sorted.Take(per).Average(r => r.Fwd),+6:+0.00;-0.00;0} ATR   " +
                              $"net-long quintile {sorted.TakeLast(per).Average(r => r.Fwd),+6:+0.00;-0.00;0} ATR   " +
                              $"gap {gap,+6:+0.00;-0.00;0}   p = {p:0.0000}" + (p <= 0.05 ? "  *" : "") +
                              $"   (baseline {baseline:+0.00;-0.00;0}, n={rows.Count:N0})");
            Console.WriteLine($"               {(gap > 0 ? "sign MATCHES the contrarian claim" : "sign is BACKWARDS — extreme longs did better")}");
        }
        Console.WriteLine();
    }

    // ── 2. Bitcoin halvings ──────────────────────────────────────────────────

    private static void Halvings(string dir, string tf, int permutations)
    {
        Console.WriteLine("  ══════ Bitcoin halvings ══════");
        // Exactly known block-reward halving dates. Four events is a tiny sample and the honest
        // framing is descriptive: this cannot support a p-value worth quoting.
        var dates = new[] { new DateTime(2012, 11, 28), new DateTime(2016, 7, 9),
                            new DateTime(2020, 5, 11), new DateTime(2024, 4, 20) };

        var pf = Directory.GetFiles(dir, $"*_{tf}.json")
            .FirstOrDefault(f => Path.GetFileName(f).Contains("bitstamp_BTC_USDT", StringComparison.OrdinalIgnoreCase));
        if (pf == null) { Console.WriteLine("    no BTC snapshot"); Console.WriteLine(); return; }

        var bars = SnapshotCommand.Load(pf).Bars;
        Console.WriteLine($"    {"halving",12} {"−180d",9} {"−90d",9} {"+90d",9} {"+180d",9} {"+365d",9}");
        foreach (var d in dates)
        {
            int i = bars.FindIndex(b => b.Date >= d);
            if (i < 0) continue;
            string Ret(int off)
            {
                int j = i + off;
                if (j < 0 || j >= bars.Count || bars[i].Close <= 0) return "n/a";
                return $"{Math.Exp(Math.Log(bars[j].Close / bars[i].Close)) - 1:+0%;-0%;0}";
            }
            Console.WriteLine($"    {d,12:yyyy-MM-dd} {Ret(-180),9} {Ret(-90),9} {Ret(90),9} {Ret(180),9} {Ret(365),9}");
        }
        Console.WriteLine("    Four events. Whatever the pattern looks like, n=4 cannot distinguish it from");
        Console.WriteLine("    chance — and all four sit inside a single secular bull market. Descriptive only.");
        Console.WriteLine();
    }

    // ── 3. Calendar structure ────────────────────────────────────────────────

    private static void Calendar(string dir, string tf, int horizon, int permutations)
    {
        Console.WriteLine("  ══════ calendar structure (needs no external data) ══════");

        foreach (var (pat, label) in new[] { ("yahoo_SPY", "SPY"), ("bitstamp_BTC_USDT", "BTC") })
        {
            var pf = Directory.GetFiles(dir, $"*_{tf}.json")
                .FirstOrDefault(f => Path.GetFileName(f).Contains(pat, StringComparison.OrdinalIgnoreCase));
            if (pf == null) continue;
            var bars = SnapshotCommand.Load(pf).Bars;
            if (bars.Count < 1000) continue;

            var rets = new List<(DateTime D, double R)>();
            for (int i = 1; i < bars.Count; i++)
                if (bars[i].Close > 0 && bars[i - 1].Close > 0)
                    rets.Add((bars[i].Date, Math.Log(bars[i].Close / bars[i - 1].Close)));

            double all = rets.Average(r => r.R);
            Console.WriteLine($"    ── {label} ({rets.Count:N0} days, mean daily {all:+0.000%;-0.000%;0}) ──");

            // Turn of month: last 1 + first 3 trading days, the classic documented window.
            var tom = rets.Where(r => r.D.Day <= 3 || r.D.Day >= 28).ToList();
            var rest = rets.Where(r => !(r.D.Day <= 3 || r.D.Day >= 28)).ToList();
            double gap = tom.Average(r => r.R) - rest.Average(r => r.R);
            double p = PermutationP(rets.Select(r => r.R).ToArray(), tom.Count, rest.Count, gap, permutations);
            Console.WriteLine($"      turn of month: {tom.Average(r => r.R),+8:+0.000%;-0.000%;0} vs rest {rest.Average(r => r.R),+8:+0.000%;-0.000%;0}   " +
                              $"gap {gap,+8:+0.000%;-0.000%;0}   p = {p:0.0000}" + (p <= 0.05 ? "  *" : ""));

            Console.Write("      by weekday: ");
            foreach (var g in rets.GroupBy(r => r.D.DayOfWeek).OrderBy(g => (int)g.Key))
                if (g.Count() > 100) Console.Write($"{g.Key.ToString()[..3]} {g.Average(r => r.R):+0.00%;-0.00%;0}  ");
            Console.WriteLine();
        }
        Console.WriteLine("    Calendar effects are the most data-mined patterns in finance. Any single");
        Console.WriteLine("    significant cell here should be read against how many cells were examined.");
        Console.WriteLine();
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static List<(long Ts, double V)>? LoadXs(string dir, string file)
    {
        var f = Path.Combine(dir, file);
        if (!File.Exists(f)) return null;
        try
        {
            var x = JsonConvert.DeserializeObject<XsFile>(File.ReadAllText(f));
            return x?.Points.Select(p => (p.Ts, p.Value)).OrderBy(p => p.Ts).ToList();
        }
        catch { return null; }
    }

    private static double[] AlignLagged(List<(long Ts, double V)> ticks, IReadOnlyList<Ohlcv> bars, int lagDays)
    {
        var outp = new double[bars.Count];
        Array.Fill(outp, double.NaN);
        int idx = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            long cutoff = new DateTimeOffset(bars[i].Date.AddDays(-lagDays), TimeSpan.Zero).ToUnixTimeMilliseconds();
            while (idx + 1 < ticks.Count && ticks[idx + 1].Ts <= cutoff) idx++;
            if (ticks[idx].Ts <= cutoff) outp[i] = ticks[idx].V;
        }
        return outp;
    }

    /// <summary>
    /// Two-sample permutation test — see <see cref="LabStats.PermutationP"/>. The seed lives here,
    /// not in the shared helper, because it is this command's research parameter.
    /// Capped at 4,000 permutations: this command runs the test inside a loop over
    /// many buckets, and the full count would dominate its runtime.
    /// </summary>
    private static double PermutationP(double[] pool, int nA, int nB, double observed, int runs) =>
        LabStats.PermutationP(pool, nA, nB, observed, runs, seed: 4747, cap: 4_000);
}
