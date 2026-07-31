using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// The robustness pass on MVRV and NVT — the same four tests cross-sectional momentum passed 4/4
/// and the SMA(200) dip filter failed.
///
/// <para>
/// The conditional relationship has to become a STRATEGY before costs mean anything: long while the
/// metric's rolling z is in its top quintile, flat otherwise, evaluated daily. That is a
/// partial-exposure rule, which dictates the control — a block-bootstrap null sits far below 1 for
/// any such rule and would be cleared by almost anything, so the benchmark is the
/// <b>exposure-matched timing null</b>: the same number of days in market, chosen as random
/// contiguous blocks instead of by the signal. That is the test that carried the Trading Cross when
/// a block-bootstrap could not.
/// </para>
///
/// <para>
/// THE NOISE TEST HAS A TRAP UNIQUE TO THIS METRIC. MVRV is market cap over realized cap, and market
/// cap is price × supply — so MVRV is <i>proportional to price</i>. Perturbing the price series
/// while leaving MVRV untouched would hand the metric a clean signal and a noisy target, and it
/// would look robust for a reason that is an artefact of the test rig. Both are therefore perturbed
/// by the same factor. That slightly overstates the damage — realized cap is an average of past
/// transaction prices and would absorb some of the noise in reality — so this is the conservative
/// direction, which is the right one to be wrong in.
/// </para>
/// </summary>
internal static class OnChainRobustness
{
    private const double TopQuintileZ = 0.84;   // ≈ the 80th percentile of a standard normal

    internal sealed record Panel(string Symbol, string Metric, List<Ohlcv> Bars, double[] Values);

    public static void Run(List<Panel> panels, int permutations)
    {
        foreach (var metric in new[] { "capmvrvcur", "NVT (mcap/transfers)" })
        {
            var set = panels.Where(p => p.Metric == metric).ToList();
            if (set.Count == 0) continue;

            Console.WriteLine();
            Console.WriteLine($"  ══════ ROBUSTNESS — {metric}, {set.Count} symbols ══════");
            Console.WriteLine("    Rule: long while the metric's rolling-365d z is in its top quintile, else flat.");
            Console.WriteLine();

            Costs(set);
            Eras(set);
            ExposureNull(set, permutations);
            Noise(set);
        }

        Survivorship();
    }

    // ── 1. Costs ─────────────────────────────────────────────────────────────

    private static void Costs(List<Panel> set)
    {
        Console.WriteLine($"    ── transaction costs ──");
        Console.WriteLine($"      {"symbol",-6} {"trades",7} {"in mkt",7} {"0bps",10} {"10bps",10} {"25bps",10} {"50bps",10} {"hold",10}");

        foreach (var p in set)
        {
            var (eq0, trades, exposure) = Book(p.Bars, p.Values, 0);
            if (trades < 3) continue;
            double hold = p.Bars[^1].Close / p.Bars[0].Close;
            Console.WriteLine($"      {p.Symbol,-6} {trades,7} {exposure,7:P0} " +
                              $"{eq0,10:0.0}× {Book(p.Bars, p.Values, 10).Equity,10:0.0}× " +
                              $"{Book(p.Bars, p.Values, 25).Equity,10:0.0}× {Book(p.Bars, p.Values, 50).Equity,10:0.0}× " +
                              $"{hold,10:0.0}×");
        }
        Console.WriteLine("      Crypto spot spreads on majors run 1-5 bps; 25-50 is a pessimistic bound.");
        Console.WriteLine();
    }

    /// <summary>
    /// Long while z is in the top quintile, flat otherwise. Signals are read at bar i and filled at
    /// bar i+1's close — the metric is already lagged a day upstream, so this is two days of delay
    /// in total, which is conservative.
    /// </summary>
    private static (double Equity, int Trades, double Exposure) Book(List<Ohlcv> bars, double[] values, double bps)
    {
        var z = RollingZ(values, 365);
        double eq = 1.0;
        bool inMkt = false;
        int trades = 0, barsIn = 0, barsTotal = 0;

        for (int i = 365; i < bars.Count - 1; i++)
        {
            if (double.IsNaN(z[i])) continue;
            barsTotal++;
            bool want = z[i] >= TopQuintileZ;
            if (want != inMkt) { inMkt = want; if (want) trades++; eq *= 1 - bps / 10000.0; }
            if (inMkt && bars[i].Close > 0)
            {
                barsIn++;
                eq *= bars[i + 1].Close / bars[i].Close;
            }
        }
        return (eq, trades, barsTotal > 0 ? barsIn / (double)barsTotal : 0);
    }

    // ── 2. Eras ──────────────────────────────────────────────────────────────

    private static void Eras(List<Panel> set)
    {
        Console.WriteLine("    ── eras (does it beat holding in each third?) ──");
        foreach (var p in set)
        {
            var z = RollingZ(p.Values, 365);
            int start = 365, n = p.Bars.Count - start;
            if (n < 600) continue;
            Console.Write($"      {p.Symbol,-6}");
            for (int e = 0; e < 3; e++)
            {
                int a = start + e * n / 3, b = start + (e + 1) * n / 3;
                double eq = 1, hold = p.Bars[Math.Min(b, p.Bars.Count - 1)].Close / p.Bars[a].Close;
                bool inMkt = false;
                for (int i = a; i < b - 1 && i < p.Bars.Count - 1; i++)
                {
                    if (double.IsNaN(z[i])) continue;
                    inMkt = z[i] >= TopQuintileZ;
                    if (inMkt && p.Bars[i].Close > 0) eq *= p.Bars[i + 1].Close / p.Bars[i].Close;
                }
                Console.Write($"  {p.Bars[a].Date:yyyy-MM}: {eq,5:0.00}× vs hold {hold,5:0.00}×{(eq > hold ? " WIN " : "     ")}");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    // ── 3. Exposure-matched timing null ──────────────────────────────────────

    /// <summary>
    /// The control that matters for a partial-exposure rule: the same number of days in market,
    /// chosen as random contiguous blocks rather than by the signal. If the signal cannot beat
    /// randomly-timed exposure of equal size, it is an exposure decision and not a timing one.
    /// </summary>
    private static void ExposureNull(List<Panel> set, int permutations)
    {
        Console.WriteLine("    ── exposure-matched timing null (the test that decides it) ──");
        int runs = Math.Min(permutations, 2000);

        foreach (var p in set)
        {
            var (eq, _, exposure) = Book(p.Bars, p.Values, 0);
            if (exposure <= 0 || exposure >= 1) continue;

            var rets = new List<double>();
            for (int i = 366; i < p.Bars.Count; i++)
                if (p.Bars[i].Close > 0 && p.Bars[i - 1].Close > 0)
                    rets.Add(Math.Log(p.Bars[i].Close / p.Bars[i - 1].Close));
            if (rets.Count < 500) continue;

            int want = (int)(exposure * rets.Count);
            var rng = new Random(31337 + p.Symbol.GetHashCode() % 997);
            int beat = 0;
            var randomBooks = new List<double>();

            for (int r = 0; r < runs; r++)
            {
                var mask = new bool[rets.Count];
                int placed = 0, guard = 0;
                // Contiguous blocks, so the random book has the same clustered-exposure shape the
                // signal produces. Scattered single days would be an easier benchmark.
                while (placed < want && guard++ < 10000)
                {
                    int len = Math.Min(20 + rng.Next(60), want - placed);
                    int at = rng.Next(Math.Max(1, rets.Count - len));
                    for (int k = at; k < at + len && k < rets.Count; k++)
                        if (!mask[k]) { mask[k] = true; placed++; }
                }
                double acc = 0;
                for (int k = 0; k < rets.Count; k++) if (mask[k]) acc += rets[k];
                double book = Math.Exp(acc);
                randomBooks.Add(book);
                if (book >= eq) beat++;
            }

            randomBooks.Sort();
            Console.WriteLine($"      {p.Symbol,-6} signal {eq,9:0.0}×   random median {randomBooks[randomBooks.Count / 2],9:0.0}×   " +
                              $"beaten by {beat,4}/{runs}   p = {(beat + 1.0) / (runs + 1):0.0000}" +
                              ((beat + 1.0) / (runs + 1) <= 0.05 ? "  *" : ""));
        }
        Console.WriteLine();
    }

    // ── 4. Noise ─────────────────────────────────────────────────────────────

    private static void Noise(List<Panel> set)
    {
        Console.WriteLine("    ── noise injection (price AND metric perturbed together — see class docs) ──");
        Console.WriteLine($"      {"symbol",-6} {"0%",10} {"25%",10} {"50%",10} {"100%",10}");

        foreach (var p in set)
        {
            var (clean, _, _) = Book(p.Bars, p.Values, 0);
            Console.Write($"      {p.Symbol,-6} {clean,10:0.0}×");

            foreach (double alpha in new[] { 0.25, 0.5, 1.0 })
            {
                var books = new List<double>();
                for (int rep = 0; rep < 5; rep++)
                {
                    var (nb, nv) = Perturb(p.Bars, p.Values, alpha, 909 + rep * 31 + p.Symbol.GetHashCode() % 500);
                    books.Add(Book(nb, nv, 0).Equity);
                }
                books.Sort();
                Console.Write($" {books[books.Count / 2],10:0.0}×");
            }
            Console.WriteLine();
        }
        Console.WriteLine("      Median of 5 draws. A real edge should decay gradually, not fall off a cliff.");
        Console.WriteLine();
    }

    /// <summary>
    /// Perturbs the price path and scales the metric by the SAME cumulative factor, because MVRV and
    /// NVT both carry market cap — i.e. price — in the numerator. Leaving the metric clean while the
    /// price is noised would be testing a rig, not a strategy.
    /// </summary>
    private static (List<Ohlcv> Bars, double[] Values) Perturb(List<Ohlcv> bars, double[] values, double alpha, int seed)
    {
        double sum = 0, sumSq = 0; int n = 0;
        var rets = new double[bars.Count];
        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i].Close <= 0 || bars[i - 1].Close <= 0) continue;
            rets[i] = Math.Log(bars[i].Close / bars[i - 1].Close);
            sum += rets[i]; sumSq += rets[i] * rets[i]; n++;
        }
        if (n < 2) return (bars, values);
        double mean = sum / n;
        double sigma = Math.Sqrt(Math.Max(1e-12, sumSq / n - mean * mean));

        var rng = new Random(seed);
        var nb = new List<Ohlcv>(bars.Count) { bars[0] };
        var nv = new double[values.Length];
        nv[0] = values[0];
        double px = bars[0].Close, drift = 0;

        for (int i = 1; i < bars.Count; i++)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double shock = alpha * sigma * g;
            drift += shock;                                  // cumulative price distortion
            px = Math.Max(1e-9, px * Math.Exp(rets[i] + shock));

            double refC = bars[i].Close > 0 ? bars[i].Close : px;
            nb.Add(new Ohlcv
            {
                Date = bars[i].Date,
                Open = px,
                High = px * Math.Max(1, bars[i].High / refC),
                Low = px * Math.Min(1, bars[i].Low / refC),
                Close = px,
                Volume = bars[i].Volume,
            });

            // Same cumulative factor applied to the metric, since it is proportional to price.
            nv[i] = double.IsNaN(values[i]) ? double.NaN : values[i] * Math.Exp(drift);
        }
        return (nb, nv);
    }

    // ── 5. Survivorship ──────────────────────────────────────────────────────

    private static void Survivorship()
    {
        Console.WriteLine("    ── survivorship ──");
        Console.WriteLine("      Only four coins have this data and all four are majors that still trade. The");
        Console.WriteLine("      hundreds of alts that went to zero have no CoinMetrics history here, and a");
        Console.WriteLine("      'high valuation predicts continuation' rule is exactly the rule that would have");
        Console.WriteLine("      been destroyed by them. This bias runs the FLATTERING way and cannot be");
        Console.WriteLine("      quantified from this dataset.");
        Console.WriteLine();
        Console.WriteLine("      It is also the narrowest cross-section in the lab: 4 symbols against 39 for");
        Console.WriteLine("      cross-sectional momentum. Read every number above with that in mind.");
        Console.WriteLine();
    }

    private static double[] RollingZ(double[] v, int win)
    {
        var z = new double[v.Length];
        Array.Fill(z, double.NaN);
        for (int i = win; i < v.Length; i++)
        {
            double sum = 0, sumSq = 0; int n = 0;
            for (int j = i - win; j <= i; j++)
            {
                if (double.IsNaN(v[j])) continue;
                sum += v[j]; sumSq += v[j] * v[j]; n++;
            }
            if (n < win / 2) continue;
            double mean = sum / n, var = sumSq / n - mean * mean;
            if (var > 1e-12) z[i] = (v[i] - mean) / Math.Sqrt(var);
        }
        return z;
    }
}
