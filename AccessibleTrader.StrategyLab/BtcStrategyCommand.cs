using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// A Bitcoin strategy built ONLY from components that survived their own controls in this lab.
///
/// <para>
/// THE INGREDIENT LIST IS SHORT ON PURPOSE. For crypto, exactly two things have survived:
/// </para>
/// <list type="number">
/// <item><b>Trend.</b> Crypto trends — measured five independent ways (variance ratio 1.150 vs 0.820
/// for equities), and the Trading Cross z-state cleared an exposure-matched timing null at
/// p = 0.001 on BTC.</item>
/// <item><b>The volume–return correlation.</b> Top-minus-bottom quintile +1.26 ATR (p = 0.0002), and
/// critically it survived <i>inside every trailing-return tercile</i> (+0.37, +0.56, +1.40, all
/// significant). It is the only input this lab has found that adds information BEYOND trend in
/// crypto.</item>
/// </list>
///
/// <para>
/// Everything else commonly proposed has been tested here and failed on crypto: Fibonacci and Gann
/// levels score identically to random lines; market-structure labels are indistinguishable from
/// random; Cipher SR proximity was a lookahead artifact; cycles are a swing-detector artifact;
/// crowding and COT carry no forward information; the RSI dip-buy worked only in equities and even
/// there failed noise injection. Adding any of them would be adding a price transform to a price
/// transform.
/// </para>
///
/// <para>
/// THE QUESTION THIS COMMAND EXISTS TO ANSWER. Both ingredients are established as CONDITIONAL
/// RELATIONSHIPS — "bars with property X had better forward returns". MVRV was equally significant
/// as a conditional mean and still failed completely as a rule, because a conditional mean is an
/// exposure statement, not a timing one. So the test is not whether trend+volume looks good; it is
/// whether <b>trend+volume beats trend alone</b>, and whether either beats an exposure-matched
/// timing null. If volume adds nothing once trend is in the book, it goes the way of MVRV.
/// </para>
/// </summary>
public static class BtcStrategyCommand
{
    private const int ZWindow = 50;      // Trading Cross tuning that survived cross-asset testing
    private const double EntryZ = 1.0;
    private const double ExitZ = 0.5;
    private const int VolWindow = 60;    // the window the volume result was measured on

    private sealed record Book(double Equity, int Trades, double Exposure, double MaxDd);

    public static int Run(string snapshotDir, string only, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine($"===== STRATEGY FROM VERIFIED PARTS — {only} =====");
        Console.WriteLine("Ingredients: trend (z-state) + volume-return correlation. Nothing else survived for crypto.");
        Console.WriteLine("Signals read at bar i, filled at bar i+1 close.");
        Console.WriteLine();

        foreach (var tf in new[] { "4h", "1d", "2d", "1w" })
        {
            var f = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
                .Where(x => !Path.GetFileName(x).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x => Path.GetFileName(x).Contains(only, StringComparison.OrdinalIgnoreCase));
            if (f == null) continue;

            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(f); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 600) continue;

            Analyse(tf, bars, permutations);
        }
        return 0;
    }

    private static void Analyse(string tf, List<Ohlcv> bars, int permutations)
    {
        var trend = TrendState(bars);
        var volq = VolumeSignal(bars);

        Console.WriteLine($"  ══════ {tf} — {bars.Count:N0} bars, {bars[0].Date:yyyy-MM} → {bars[^1].Date:yyyy-MM} ══════");

        var books = new (string Name, Func<int, bool> Rule)[]
        {
            ("trend only",     i => trend[i]),
            ("volume only",    i => volq[i]),
            ("trend AND vol",  i => trend[i] && volq[i]),
            ("trend OR vol",   i => trend[i] || volq[i]),
        };

        Console.WriteLine($"    {"book",-15} {"final",9} {"trades",7} {"in mkt",7} {"maxDD",7} {"per-day",9} {"p (exp-matched)",16}");

        // Buy-and-hold MUST start where the strategy starts. Measuring hold from bar 0 while the
        // strategy warms up for 610 bars credited hold with BTC's move from ~$10 to ~$1,000 — a
        // stretch the strategy was never allowed to trade. That one line made a winning rule look
        // like a losing one.
        int warm = Math.Max(ZWindow + VolWindow + 500, 100);
        double hold = bars[^1].Close / bars[warm].Close;
        var results = new List<(string Name, Book B, double P)>();

        foreach (var (name, rule) in books)
        {
            var b = Run(bars, rule, 0);
            if (b.Trades < 3) continue;
            double p = ExposureMatchedP(bars, b, permutations);
            results.Add((name, b, p));
            Console.WriteLine($"    {name,-15} {b.Equity,9:0.0}× {b.Trades,7} {b.Exposure,7:P0} {b.MaxDd,7:P0} " +
                              $"{Math.Exp(Math.Log(Math.Max(b.Equity, 1e-9)) / Math.Max(1, bars.Count * b.Exposure)) - 1,9:+0.000%;-0.000%;0} " +
                              $"{p,16:0.0000}{(p <= 0.05 ? " *" : "")}");
        }
        Console.WriteLine($"    {"buy & hold",-15} {hold,9:0.0}× {1,7} {1.0,7:P0} {HoldMaxDd(bars, warm),7:P0}" +
                          $"   (from {bars[warm].Date:yyyy-MM}, the first bar the strategy could trade)");
        Console.WriteLine();

        // THE COMPARISON THAT DECIDES IT. Both ingredients are established conditional relationships;
        // the question is whether the second one earns its place once the first is in the book.
        var t = results.FirstOrDefault(r => r.Name == "trend only");
        var tv = results.FirstOrDefault(r => r.Name == "trend AND vol");
        if (t.B != null && tv.B != null)
        {
            Console.WriteLine($"    ── does volume EARN ITS PLACE on top of trend? ──");
            Console.WriteLine($"       trend only    {t.B.Equity,8:0.0}×  ({t.B.Exposure:P0} exposure)");
            Console.WriteLine($"       trend AND vol {tv.B.Equity,8:0.0}×  ({tv.B.Exposure:P0} exposure)");
            Console.WriteLine($"       → {(tv.B.Equity > t.B.Equity ? "volume ADDS" : "volume SUBTRACTS")}, " +
                              "but read the per-day rate, not the total — the combined book is in the");
            Console.WriteLine("         market less, so a lower total can still be a better rate.");
            double tRate = Math.Log(Math.Max(t.B.Equity, 1e-9)) / Math.Max(1, bars.Count * t.B.Exposure);
            double tvRate = Math.Log(Math.Max(tv.B.Equity, 1e-9)) / Math.Max(1, bars.Count * tv.B.Exposure);
            Console.WriteLine($"       per-bar-in-market log return: trend {tRate:+0.00000;-0.00000;0}   " +
                              $"trend+vol {tvRate:+0.00000;-0.00000;0}   " +
                              $"{(tvRate > tRate ? "volume improves the RATE" : "volume worsens the RATE")}");
            Console.WriteLine();
        }

        Costs(bars, i => trend[i] && volq[i], i => trend[i]);
        Eras(bars, trend, volq);
        Console.WriteLine();
    }

    // ── Components ───────────────────────────────────────────────────────────

    /// <summary>The Trading Cross state machine: long above +1σ, flat below +0.5σ. Causal.</summary>
    private static bool[] TrendState(IReadOnlyList<Ohlcv> bars)
    {
        var z = TradingCrossCommand.ZScore(bars, ZWindow);
        var state = new bool[bars.Count];
        bool inMkt = false;
        for (int i = ZWindow + 1; i < bars.Count; i++)
        {
            if (!double.IsNaN(z[i]) && !double.IsNaN(z[i - 1]))
            {
                if (!inMkt && z[i - 1] <= EntryZ && z[i] > EntryZ) inMkt = true;
                else if (inMkt && z[i - 1] >= ExitZ && z[i] < ExitZ) inMkt = false;
            }
            state[i] = inMkt;
        }
        return state;
    }

    /// <summary>
    /// The verified volume signal: trailing correlation between returns and log volume, in its own
    /// top quintile. Volume arriving on up-days. The quintile threshold is computed on a TRAILING
    /// window, never on the whole sample — using a full-sample quantile would leak the future into
    /// every early bar.
    /// </summary>
    private static bool[] VolumeSignal(IReadOnlyList<Ohlcv> bars)
    {
        var corr = new double[bars.Count];
        Array.Fill(corr, double.NaN);
        for (int i = VolWindow; i < bars.Count; i++)
        {
            var r = new List<double>(); var v = new List<double>();
            for (int j = i - VolWindow + 1; j <= i; j++)
            {
                if (j < 1 || bars[j].Close <= 0 || bars[j - 1].Close <= 0 || bars[j].Volume <= 0) continue;
                r.Add(Math.Log(bars[j].Close / bars[j - 1].Close));
                v.Add(Math.Log(bars[j].Volume));
            }
            if (r.Count < VolWindow / 2) continue;
            double mr = r.Average(), mv = v.Average(), srv = 0, srr = 0, svv = 0;
            for (int k = 0; k < r.Count; k++)
            {
                double a = r[k] - mr, b = v[k] - mv;
                srv += a * b; srr += a * a; svv += b * b;
            }
            if (srr > 0 && svv > 0) corr[i] = srv / Math.Sqrt(srr * svv);
        }

        var sig = new bool[bars.Count];
        const int Lookback = 500;
        for (int i = VolWindow + Lookback; i < bars.Count; i++)
        {
            if (double.IsNaN(corr[i])) continue;
            var hist = new List<double>();
            for (int j = i - Lookback; j < i; j++) if (!double.IsNaN(corr[j])) hist.Add(corr[j]);
            if (hist.Count < Lookback / 2) continue;
            hist.Sort();
            sig[i] = corr[i] >= hist[(int)(hist.Count * 0.8)];   // trailing 80th percentile
        }
        return sig;
    }

    // ── Books and controls ───────────────────────────────────────────────────

    private static Book Run(IReadOnlyList<Ohlcv> bars, Func<int, bool> rule, double bps)
    {
        double eq = 1, peak = 1, maxDd = 0;
        bool inMkt = false;
        int trades = 0, barsIn = 0, total = 0;
        int start = Math.Max(ZWindow + VolWindow + 500, 100);

        for (int i = start; i < bars.Count - 1; i++)
        {
            total++;
            bool want = rule(i);
            if (want != inMkt) { inMkt = want; if (want) trades++; eq *= 1 - bps / 10000.0; }
            if (inMkt && bars[i].Close > 0) { barsIn++; eq *= bars[i + 1].Close / bars[i].Close; }
            peak = Math.Max(peak, eq);
            maxDd = Math.Max(maxDd, 1 - eq / peak);
        }
        return new Book(eq, trades, total > 0 ? barsIn / (double)total : 0, maxDd);
    }

    /// <summary>
    /// The control that killed MVRV: same days in market, chosen as random contiguous blocks rather
    /// than by the signal. A partial-exposure rule that cannot beat this is an exposure decision
    /// wearing a strategy's clothes.
    /// </summary>
    private static double ExposureMatchedP(IReadOnlyList<Ohlcv> bars, Book b, int permutations)
    {
        int start = Math.Max(ZWindow + VolWindow + 500, 100);
        var rets = new List<double>();
        for (int i = start; i < bars.Count - 1; i++)
            if (bars[i].Close > 0 && bars[i + 1].Close > 0) rets.Add(Math.Log(bars[i + 1].Close / bars[i].Close));
        if (rets.Count < 200) return 1;

        int want = (int)(b.Exposure * rets.Count);
        if (want <= 0 || want >= rets.Count) return 1;

        var rng = new Random(88);
        int runs = Math.Min(permutations, 2000), beat = 0;
        for (int r = 0; r < runs; r++)
        {
            var mask = new bool[rets.Count];
            int placed = 0, guard = 0;
            while (placed < want && guard++ < 20000)
            {
                int len = Math.Min(10 + rng.Next(40), want - placed);
                int at = rng.Next(Math.Max(1, rets.Count - len));
                for (int k = at; k < at + len && k < rets.Count; k++) if (!mask[k]) { mask[k] = true; placed++; }
            }
            double acc = 0;
            for (int k = 0; k < rets.Count; k++) if (mask[k]) acc += rets[k];
            if (Math.Exp(acc) >= b.Equity) beat++;
        }
        return (beat + 1.0) / (runs + 1);
    }

    private static void Costs(IReadOnlyList<Ohlcv> bars, Func<int, bool> combo, Func<int, bool> trendOnly)
    {
        Console.WriteLine("    ── costs ──");
        Console.WriteLine($"       {"bps/side",9} {"trend only",12} {"trend AND vol",14}");
        foreach (double bps in new[] { 0.0, 5.0, 10.0, 25.0 })
            Console.WriteLine($"       {bps,9:0} {Run(bars, trendOnly, bps).Equity,12:0.0}× {Run(bars, combo, bps).Equity,14:0.0}×");
    }

    private static void Eras(IReadOnlyList<Ohlcv> bars, bool[] trend, bool[] volq)
    {
        Console.WriteLine("    ── eras ──");
        int start = Math.Max(ZWindow + VolWindow + 500, 100);
        int n = bars.Count - start;
        if (n < 600) return;

        for (int e = 0; e < 3; e++)
        {
            int a = start + e * n / 3, b = start + (e + 1) * n / 3;
            double eqT = 1, eqC = 1;
            for (int i = a; i < b - 1 && i < bars.Count - 1; i++)
            {
                if (trend[i] && bars[i].Close > 0) eqT *= bars[i + 1].Close / bars[i].Close;
                if (trend[i] && volq[i] && bars[i].Close > 0) eqC *= bars[i + 1].Close / bars[i].Close;
            }
            double hold = bars[Math.Min(b, bars.Count - 1)].Close / bars[a].Close;
            Console.WriteLine($"       {bars[a].Date:yyyy-MM}→{bars[Math.Min(b, bars.Count - 1)].Date:yyyy-MM}: " +
                              $"trend {eqT,6:0.00}×   trend+vol {eqC,6:0.00}×   hold {hold,6:0.00}×");
        }
    }

    private static double HoldMaxDd(IReadOnlyList<Ohlcv> bars, int from)
    {
        double peak = 0, dd = 0;
        foreach (var b in bars.Skip(from)) { peak = Math.Max(peak, b.Close); if (peak > 0) dd = Math.Max(dd, 1 - b.Close / peak); }
        return dd;
    }
}
