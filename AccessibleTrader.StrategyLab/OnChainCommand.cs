using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests on-chain VALUE metrics — the first genuinely non-price alpha family this lab has looked at,
/// and the closest thing crypto has to fundamentals.
///
/// <para>
/// WHY THIS FAMILY. Every null produced here so far has been a price-derived, single-asset,
/// time-series signal or a conditioner on one: confluence stacking, the z-state gate, crowding,
/// cycles, the capitulation claim. The single thing that survived a full robustness pass —
/// cross-sectional momentum — was a FAMILY change rather than a better variation. On-chain data is
/// the next family with real data behind it: 4,116 days of Bitcoin back to 2015.
/// </para>
///
/// <para>
/// THE HEADLINE METRIC. MVRV is market cap over REALIZED cap, where realized cap values every coin
/// at the price it last moved on-chain. The denominator is therefore the aggregate cost basis of
/// the network — information that exists nowhere in a price series. The folklore is that MVRV above
/// ~3.7 marks tops and below ~1 marks bottoms.
/// </para>
///
/// <para>
/// THE CONTROL, DESIGNED IN FROM THE START. Market cap is price × supply, and supply barely moves,
/// so MVRV is structurally close to "price over a slowly-updating baseline" — which is the Trading
/// Cross z-score wearing different clothes. This lab has already been fooled twice by exactly that
/// shape: the crowding index documented itself as orthogonal to price while correlating 0.19 with
/// trailing returns, and the volume signal correlated 0.43–0.59. So the question here is NOT "does
/// MVRV predict returns" — it is <b>"does MVRV beat a price-over-moving-average baseline of the
/// same speed?"</b> The matched MA length is measured from the data rather than assumed.
/// </para>
///
/// <para>
/// Metrics are lagged one day. CoinMetrics stamps a metric with the day it describes, but a day's
/// on-chain aggregate is not knowable until that day has closed.
/// </para>
/// </summary>
public static class OnChainCommand
{
    private const int MetricLagDays = 1;

    private sealed record Obs(string Symbol, string Metric, double Value, double Z,
        double MaRatio, double FwdRet, double FwdAtr, DateTime Date);

    private sealed class XsFile
    {
        public string Symbol { get; set; } = "";
        public List<XsPoint> Points { get; set; } = new();
    }

    private sealed class XsPoint { public long Ts { get; set; } public double Value { get; set; } }

    public static int Run(string snapshotDir, string tf, int horizon, int permutations)
    {
        var pairs = new (string Price, string Chain, string Label)[]
        {
            ("bitstamp_BTC_USDT", "btc", "BTC"),
            ("bitstamp_ETH_USDT", "eth", "ETH"),
            ("bitstamp_LTC_USDT", "ltc", "LTC"),
            ("bitstamp_XRP_USDT", "xrp", "XRP"),
        };

        var obs = new List<Obs>();
        var panels = new List<OnChainRobustness.Panel>();
        Console.WriteLine();
        Console.WriteLine($"===== ON-CHAIN VALUE METRICS — forward horizon {horizon} bars =====");
        Console.WriteLine($"Metrics lagged {MetricLagDays}d (a day's on-chain aggregate is not knowable until it closes).");
        Console.WriteLine();

        foreach (var (pricePat, chain, label) in pairs)
        {
            var pf = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
                .FirstOrDefault(f => Path.GetFileName(f).StartsWith(pricePat, StringComparison.OrdinalIgnoreCase));
            if (pf == null) continue;

            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(pf); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 500) continue;

            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);

            foreach (var metric in new[] { "capmvrvcur", "adractcnt", "txcnt", "txtfrcnt", "hashrate" })
            {
                var raw = LoadXs(snapshotDir, chain, metric);
                if (raw == null) continue;

                var aligned = Align(raw, bars);
                int real = aligned.Count(v => !double.IsNaN(v));
                if (real < 500) continue;

                Build(obs, label, metric, bars, atr, aligned, horizon);
                panels.Add(new OnChainRobustness.Panel(label, metric, bars, aligned));
            }

            // Derived ratios. On-chain LEVELS grow secularly with adoption, so a raw level is
            // non-stationary and a threshold on it means something different every year. Ratios
            // against market cap are the crypto analogues of a valuation multiple.
            var mcap = LoadXs(snapshotDir, chain, "capmrktcurusd");
            var txf = LoadXs(snapshotDir, chain, "txtfrcnt");
            var addr = LoadXs(snapshotDir, chain, "adractcnt");

            if (mcap != null && txf != null)
            {
                var nvt = Ratio(Align(mcap, bars), Align(txf, bars));
                Build(obs, label, "NVT (mcap/transfers)", bars, atr, nvt, horizon);
                panels.Add(new OnChainRobustness.Panel(label, "NVT (mcap/transfers)", bars, nvt));
            }
            if (mcap != null && addr != null)
                Build(obs, label, "mcap/addresses", bars, atr, Ratio(Align(mcap, bars), Align(addr, bars)), horizon);
        }

        if (obs.Count < 2000) { Console.WriteLine($"Too few observations ({obs.Count})."); return 1; }

        Report(obs, permutations);
        OnChainRobustness.Run(panels, permutations);
        return 0;
    }

    // ── Measurement ──────────────────────────────────────────────────────────

    private static void Build(List<Obs> sink, string symbol, string metric, IReadOnlyList<Ohlcv> bars,
        double[] atr, double[] values, int horizon)
    {
        // Rolling z of the metric — a level threshold is meaningless on a secularly growing series,
        // and z-scoring is also what makes the comparison against a price z fair.
        const int ZWin = 365;
        var z = LabStats.RollingZ(values, ZWin);

        // The matched-speed price baseline. Its length is not assumed: it is the SMA whose
        // price/SMA ratio best tracks this metric, found by search. That makes "does the metric beat
        // an equivalent moving average" a fair fight rather than a straw man.
        int maLen = BestMatchedMa(values, bars);
        var maRatio = PriceOverSma(bars, maLen);

        for (int i = ZWin; i < bars.Count - horizon; i++)
        {
            if (double.IsNaN(values[i]) || double.IsNaN(z[i]) || double.IsNaN(maRatio[i])) continue;
            if (double.IsNaN(atr[i]) || atr[i] <= 0 || bars[i].Close <= 0) continue;

            sink.Add(new Obs(symbol, metric, values[i], z[i], maRatio[i],
                Math.Log(bars[i + horizon].Close / bars[i].Close),
                (bars[i + horizon].Close - bars[i].Close) / atr[i],
                bars[i].Date));
        }
    }

    /// <summary>
    /// The SMA length whose price/SMA ratio correlates most strongly with the metric. This is the
    /// baseline the metric has to beat — if a plain moving average of that speed predicts returns
    /// just as well, the on-chain data added nothing.
    /// </summary>
    private static int BestMatchedMa(double[] values, IReadOnlyList<Ohlcv> bars)
    {
        int best = 200; double bestAbs = -1;
        foreach (int len in new[] { 20, 50, 100, 200, 365, 500, 730 })
        {
            var r = PriceOverSma(bars, len);
            double c = Correlation(values, r);
            if (!double.IsNaN(c) && Math.Abs(c) > bestAbs) { bestAbs = Math.Abs(c); best = len; }
        }
        return best;
    }

    private static double[] PriceOverSma(IReadOnlyList<Ohlcv> bars, int len)
    {
        var outp = new double[bars.Count];
        Array.Fill(outp, double.NaN);
        double sum = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            sum += bars[i].Close;
            if (i >= len) sum -= bars[i - len].Close;
            if (i >= len - 1 && sum > 0) outp[i] = bars[i].Close / (sum / len);
        }
        return outp;
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    private static void Report(List<Obs> all, int permutations)
    {
        foreach (var g in all.GroupBy(o => o.Metric).OrderBy(g => g.Key))
        {
            var set = g.ToList();
            Console.WriteLine($"  ══════ {g.Key} ({set.Select(o => o.Symbol).Distinct().Count()} symbols, {set.Count:N0} obs) ══════");

            // Quintiles of the metric's own z-score.
            var byZ = set.OrderBy(o => o.Z).ToList();
            int per = byZ.Count / 5;
            for (int q = 0; q < 5; q++)
            {
                var s = byZ.Skip(q * per).Take(q == 4 ? int.MaxValue : per).ToList();
                Console.WriteLine($"    z quintile {q + 1} ({s.Min(o => o.Z),+5:+0.0;-0.0;0}…{s.Max(o => o.Z),+5:+0.0;-0.0;0}): " +
                                  $"fwd {s.Average(o => o.FwdAtr),+6:+0.00;-0.00;0} ATR   n={s.Count,6:N0}");
            }

            double metricGap = byZ.Take(per).Average(o => o.FwdAtr) - byZ.TakeLast(per).Average(o => o.FwdAtr);
            double pM = PermutationP(set.Select(o => o.FwdAtr).ToArray(), per, per, metricGap, permutations);

            // THE CONTROL, on the same rows: the matched-speed price/SMA ratio, bucketed identically.
            var byMa = set.OrderBy(o => o.MaRatio).ToList();
            double maGap = byMa.Take(per).Average(o => o.FwdAtr) - byMa.TakeLast(per).Average(o => o.FwdAtr);
            double pMa = PermutationP(set.Select(o => o.FwdAtr).ToArray(), per, per, maGap, permutations);

            Console.WriteLine($"    LOW − HIGH quintile:  metric {metricGap,+6:+0.00;-0.00;0} ATR (p={pM:0.0000})   " +
                              $"│ matched price/SMA {maGap,+6:+0.00;-0.00;0} ATR (p={pMa:0.0000})");
            Console.WriteLine($"    correlation(metric, its matched price/SMA) = {Correlation(set.Select(o => o.Value).ToArray(), set.Select(o => o.MaRatio).ToArray()):+0.000;-0.000;0}");
            Console.WriteLine($"    → {(Math.Abs(metricGap) > Math.Abs(maGap) + 0.05 ? "the metric BEATS the price baseline" : "no better than a moving average of the same speed")}");

            // Per symbol, because a pooled number can be one coin.
            var per_sym = set.GroupBy(o => o.Symbol).Select(sg =>
            {
                var l = sg.OrderBy(o => o.Z).ToList();
                int k = Math.Max(20, l.Count / 5);
                return (sg.Key, Gap: l.Take(k).Average(o => o.FwdAtr) - l.TakeLast(k).Average(o => o.FwdAtr));
            }).ToList();
            Console.WriteLine($"    per symbol: {string.Join("  ", per_sym.Select(t => $"{t.Key} {t.Gap:+0.00;-0.00;0}"))}");

            // THE CONFOUND THAT MATTERS MOST HERE. Every on-chain series grows secularly with
            // adoption, and so did the price of everything in this sample. A metric whose low
            // readings cluster early and high readings cluster late will look predictive purely
            // because the early years returned more — nothing to do with the metric. Splitting by
            // era is the only way to see it: an effect that only exists in one slice is calendar
            // time in disguise.
            Console.Write("    by era:   ");
            var byDate = set.OrderBy(o => o.Date).ToList();
            int eraN = byDate.Count / 3;
            for (int e = 0; e < 3; e++)
            {
                var era = byDate.Skip(e * eraN).Take(e == 2 ? int.MaxValue : eraN).ToList();
                // Era bounds must be read BEFORE re-sorting by z — otherwise they print the dates of
                // the lowest and highest readings rather than the slice's calendar span.
                DateTime from = era[0].Date, to = era[^1].Date;
                var slice = era.OrderBy(o => o.Z).ToList();
                int k = Math.Max(30, slice.Count / 5);
                double gap = slice.Take(k).Average(o => o.FwdAtr) - slice.TakeLast(k).Average(o => o.FwdAtr);
                double pe = PermutationP(slice.Select(o => o.FwdAtr).ToArray(), k, k, gap, permutations);
                Console.Write($"{from:yyyy-MM}→{to:yyyy-MM} {gap,+6:+0.00;-0.00;0}(p={pe:0.000})   ");
            }
            Console.WriteLine();
            Console.WriteLine();
        }

        Console.WriteLine("  ── multiple comparisons ──");
        Console.WriteLine($"    {all.Select(o => o.Metric).Distinct().Count()} metrics were tested. At alpha 0.05 that is ~0.35 false positives");
        Console.WriteLine("    expected by chance; a Bonferroni threshold would be ~0.007. Only results that clear");
        Console.WriteLine("    that AND hold across eras AND beat their matched price baseline should be believed.");
        Console.WriteLine();

        Folklore(all, permutations);
    }

    /// <summary>The specific claim in circulation: MVRV above ~3.7 marks tops, below ~1 marks bottoms.</summary>
    private static void Folklore(List<Obs> all, int permutations)
    {
        var mvrv = all.Where(o => o.Metric == "capmvrvcur").ToList();
        if (mvrv.Count < 500) return;

        Console.WriteLine("  ── the folklore thresholds: MVRV > 3.7 = top, < 1 = bottom ──");
        double baseline = mvrv.Average(o => o.FwdAtr);
        foreach (var (lo, hi, name) in new[] { (0.0, 1.0, "< 1.0  (claimed bottom)"), (1.0, 2.0, "1.0–2.0"),
                                               (2.0, 3.7, "2.0–3.7"), (3.7, 999.0, "> 3.7  (claimed top)") })
        {
            var s = mvrv.Where(o => o.Value >= lo && o.Value < hi).ToList();
            if (s.Count < 30) { Console.WriteLine($"    {name,-24} n={s.Count} — too few"); continue; }
            var rest = mvrv.Where(o => o.Value < lo || o.Value >= hi).ToList();
            double gap = s.Average(o => o.FwdAtr) - rest.Average(o => o.FwdAtr);
            double p = PermutationP(mvrv.Select(o => o.FwdAtr).ToArray(), s.Count, rest.Count, gap, permutations);
            Console.WriteLine($"    {name,-24} fwd {s.Average(o => o.FwdAtr),+6:+0.00;-0.00;0} ATR (n={s.Count,5:N0})   " +
                              $"vs rest {gap,+6:+0.00;-0.00;0} ATR   p = {p:0.0000}" + (p <= 0.05 ? "  *" : ""));
        }
        Console.WriteLine($"    (all-MVRV baseline {baseline:+0.00;-0.00;0} ATR)");
        Console.WriteLine();
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static List<(long Ts, double V)>? LoadXs(string dir, string chain, string metric)
    {
        var f = Path.Combine(dir, $"xs_coinmetrics_{chain}_{metric}_1d.json");
        if (!File.Exists(f)) return null;
        try
        {
            var x = JsonConvert.DeserializeObject<XsFile>(File.ReadAllText(f));
            return x?.Points.Select(p => (p.Ts, p.Value)).OrderBy(p => p.Ts).ToList();
        }
        catch { return null; }
    }

    /// <summary>
    /// Most recent metric value at or before (bar date − lag). Causal by construction; the lag makes
    /// it conservative, since a day's on-chain aggregate is not final until the day has closed.
    /// </summary>
    private static double[] Align(List<(long Ts, double V)> ticks, IReadOnlyList<Ohlcv> bars)
    {
        var outp = new double[bars.Count];
        Array.Fill(outp, double.NaN);
        int idx = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            long cutoff = new DateTimeOffset(bars[i].Date.AddDays(-MetricLagDays), TimeSpan.Zero).ToUnixTimeMilliseconds();
            while (idx + 1 < ticks.Count && ticks[idx + 1].Ts <= cutoff) idx++;
            if (ticks[idx].Ts <= cutoff && ticks[idx].V > 0) outp[i] = ticks[idx].V;
        }
        return outp;
    }

    private static double[] Ratio(double[] a, double[] b)
    {
        var outp = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            outp[i] = double.IsNaN(a[i]) || double.IsNaN(b[i]) || b[i] <= 0 ? double.NaN : a[i] / b[i];
        return outp;
    }

    private static double Correlation(double[] x, double[] y)
    {
        var keep = Enumerable.Range(0, Math.Min(x.Length, y.Length))
            .Where(i => !double.IsNaN(x[i]) && !double.IsNaN(y[i])).ToArray();
        if (keep.Length < 100) return double.NaN;
        double mx = keep.Average(i => x[i]), my = keep.Average(i => y[i]);
        double sxy = 0, sxx = 0, syy = 0;
        foreach (int i in keep)
        {
            double a = x[i] - mx, b = y[i] - my;
            sxy += a * b; sxx += a * a; syy += b * b;
        }
        return sxx <= 0 || syy <= 0 ? double.NaN : sxy / Math.Sqrt(sxx * syy);
    }

    /// <summary>
    /// Two-sample permutation test — see <see cref="LabStats.PermutationP"/>. The seed lives here,
    /// not in the shared helper, because it is this command's research parameter.
    /// Capped at 4,000 permutations: this command runs the test inside a loop over
    /// many buckets, and the full count would dominate its runtime.
    /// </summary>
    private static double PermutationP(double[] pool, int nA, int nB, double observed, int runs) =>
        LabStats.PermutationP(pool, nA, nB, observed, runs, seed: 6161, cap: 4_000);
}
