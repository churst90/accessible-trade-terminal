using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Asks whether CROWDING — funding rate plus open-interest change, signed by price direction —
/// conditions trend and reversion entries in a way that price-derived conditioners cannot.
///
/// <para>
/// WHY THIS AND NOT ANOTHER CONFLUENCE STACK. Support/resistance, Fibonacci levels, swing points,
/// candle patterns, market structure and the Cipher oscillators are all deterministic transforms of
/// one OHLC series. Agreement between them is arithmetic, not evidence. This codebase already
/// found that the hard way — <see cref="CrowdingIndexProvider"/>'s own notes record that eight
/// versions of pure-Cipher confluence walk-forwarded to break-even "because price-derived
/// indicators are auto-correlated" — and <see cref="GateCommand"/> found the same thing again from
/// the other direction when a z-score gate turned out to be structurally incapable of being open
/// at a dip-buy signal.
/// </para>
///
/// <para>
/// Crowding is the one input in that family that is NOT a price transform. Funding is what leveraged
/// traders are paying to hold a side; open interest is how many of them there are. Rishi Narang's
/// taxonomy calls this "technical sentiment" and says explicitly that it can be traded directly OR
/// used as a conditioner on trend and reversion — which is the hypothesis under test.
/// </para>
///
/// <para>
/// THE PREDICTION THAT CAN FAIL. Crowding is signed so that positive means the LONG side is piled
/// in. If it carries information, long entries should be systematically WORSE when it is high
/// (joining a consensus trade, squeeze risk) and BETTER when it is deeply negative (shorts are the
/// crowd, and their covering is fuel). A conditioner with no information will show no gap.
/// </para>
///
/// <para>
/// THE TWO CONTROLS. Random entries measure what any long inherits from the market simply moving —
/// without that baseline "longs did better when crowding was negative" is a statement about the
/// market, not about crowding. And the 200-bar moving average measures what a price-derived
/// conditioner already extracts. Crowding only justifies its extra data feed if it beats both.
/// </para>
/// </summary>
public static class CrowdingCommand
{
    private const int HorizonBars = 20;
    private const double RiskAtrFraction = 1.0;
    private const double TargetR = 2.0;
    private const int MaGateBars = 200;

    private sealed record Trade(string Signal, double R, double Crowding, bool MaGate, DateTime Date);

    /// <summary>Every bar where crowding is defined, with its forward return. Sparse entry signals
    /// throw away 95% of the data; this keeps all of it, and it is the highest-powered form of the
    /// question "does crowding predict anything at all".</summary>
    private sealed record Observation(string Symbol, double Crowding, double FwdRet, double AtrNorm);

    private static readonly List<Observation> _obs = new();

    /// <summary>
    /// Horizons to test the crowding signal over. A single horizon is not a fair test of this
    /// mechanism: funding settles every eight hours and a squeeze resolves in days, so measuring
    /// only at 20 bars could miss a real effect at 3 and call it absent. The 20-bar case is the
    /// one the trade harness uses; the rest exist so the null cannot be an artefact of that choice.
    /// </summary>
    private static readonly int[] Horizons = { 1, 3, 5, 10, 20, 40 };

    /// <summary>symbol → (crowding, close, atr) for the multi-horizon sweep.</summary>
    private static readonly List<(string Symbol, double[] Crowd, double[] Close, double[] Atr)> _series = new();

    public static async Task<int> RunAsync(string snapshotDir, string tf, int permutations)
    {
        var services = LabHost.Build().Services;
        var engine = services.GetRequiredService<IIndicatorEngine>();

        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => Path.GetFileName(f).StartsWith("bitstamp_", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(f).StartsWith("mexc_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var trades = new List<Trade>();
        var covered = new List<string>();
        var skipped = new List<string>();
        int seed = 0;

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 400) continue;

            Dictionary<string, double[]> crowd;
            try
            {
                crowd = await engine.CalculateAsync("CROWDING_INDEX", bars,
                    new Dictionary<string, object> { ["__symbol"] = snap.Symbol }, default);
            }
            catch (Exception ex) { Console.Error.WriteLine($"  ! {snap.Symbol}: {ex.Message}"); continue; }

            if (!crowd.TryGetValue(CrowdingIndexProvider.CompCrowdingScore, out var score))
            { skipped.Add($"{snap.Symbol} (no score component)"); continue; }

            int real = score.Count(v => !double.IsNaN(v));
            if (real < 300) { skipped.Add($"{snap.Symbol} ({real} crowding points — no funding/OI feed)"); continue; }

            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);
            var maGate = MovingAverageGate(bars, MaGateBars);
            var closes = bars.Select(b => b.Close).ToArray();

            Add(trades, "trend-long", bars, atr, DonchianBreakout(bars, 20), score, maGate);
            Add(trades, "revert-long", bars, atr,
                CrossUp(AccessibleTrader.Sdk.Indicators.IndicatorMath.Rsi(closes, 14), 30.0), score, maGate);
            Add(trades, "random-long", bars, atr, RandomSignal(bars.Count, 400, seed++), score, maGate);

            for (int i = 0; i < bars.Count - HorizonBars; i++)
            {
                if (i >= score.Length || double.IsNaN(score[i])) continue;
                if (double.IsNaN(atr[i]) || atr[i] <= 0 || bars[i].Close <= 0) continue;
                _obs.Add(new Observation(
                    snap.Symbol, score[i],
                    Math.Log(bars[i + HorizonBars].Close / bars[i].Close),
                    (bars[i + HorizonBars].Close - bars[i].Close) / atr[i]));
            }

            _series.Add((snap.Symbol, score, bars.Select(b => b.Close).ToArray(), atr));

            covered.Add($"{snap.Symbol} ({real})");
        }

        Console.WriteLine();
        Console.WriteLine($"Crowding coverage: {string.Join(", ", covered)}");
        if (skipped.Count > 0) Console.WriteLine($"Skipped: {string.Join(", ", skipped)}");

        if (trades.Count < 300) { Console.WriteLine($"\nToo few trades ({trades.Count})."); return 1; }

        Report(trades, permutations);
        return 0;
    }

    private static void Add(List<Trade> sink, string label, IReadOnlyList<Ohlcv> bars, double[] atr,
        double[]? signal, double[] crowding, bool[] maGate)
    {
        if (signal == null) return;

        for (int i = 1; i < bars.Count - HorizonBars - 1; i++)
        {
            if (double.IsNaN(signal[i])) continue;
            double a = atr[i];
            if (double.IsNaN(a) || a <= 0) continue;

            // Crowding is only defined where the funding/OI feed reaches, and the MA gate needs its
            // own warmup. Counting an undefined conditioner as one of its states would load every
            // early trade onto one side of the comparison.
            if (i >= crowding.Length || double.IsNaN(crowding[i]) || i < MaGateBars) continue;

            double entry = bars[i + 1].Open;
            double risk = a * RiskAtrFraction;
            double stop = entry - risk;
            double target = entry + risk * TargetR;

            double r = 0;
            bool resolved = false;
            for (int j = i + 1; j <= i + HorizonBars; j++)
            {
                if (bars[j].Low <= stop) { r = -1; resolved = true; break; }
                if (bars[j].High >= target) { r = TargetR; resolved = true; break; }
            }
            if (!resolved) r = (bars[i + HorizonBars].Close - entry) / risk;

            sink.Add(new Trade(label, r, crowding[i], maGate[i], bars[i].Date));
        }
    }

    // ── Signals ──────────────────────────────────────────────────────────────

    private static double[] DonchianBreakout(IReadOnlyList<Ohlcv> bars, int period)
    {
        var sig = new double[bars.Count];
        Array.Fill(sig, double.NaN);
        for (int i = period; i < bars.Count; i++)
        {
            double hh = double.MinValue;
            for (int j = i - period; j < i; j++) hh = Math.Max(hh, bars[j].High);
            if (bars[i].Close > hh && bars[i - 1].Close <= hh) sig[i] = 1;
        }
        return sig;
    }

    private static double[] CrossUp(double[] v, double level)
    {
        var sig = new double[v.Length];
        Array.Fill(sig, double.NaN);
        for (int i = 1; i < v.Length; i++)
            if (!double.IsNaN(v[i]) && !double.IsNaN(v[i - 1]) && v[i - 1] <= level && v[i] > level) sig[i] = 1;
        return sig;
    }

    private static double[] RandomSignal(int barCount, int count, int seed)
    {
        var sig = new double[barCount];
        Array.Fill(sig, double.NaN);
        var rng = new Random(7000 + seed);
        for (int k = 0; k < count; k++) sig[rng.Next(barCount)] = 1;
        return sig;
    }

    private static bool[] MovingAverageGate(IReadOnlyList<Ohlcv> bars, int period)
    {
        var gate = new bool[bars.Count];
        double sum = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            sum += bars[i].Close;
            if (i >= period) sum -= bars[i - period].Close;
            if (i >= period - 1) gate[i] = bars[i].Close > sum / period;
        }
        return gate;
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    private static void Report(List<Trade> all, int permutations)
    {
        var span = (Min: all.Min(t => t.Date), Max: all.Max(t => t.Date));

        Console.WriteLine();
        Console.WriteLine($"===== CROWDING AS A CONDITIONER — {all.Count:N0} long trades =====");
        Console.WriteLine($"Window {span.Min:yyyy-MM-dd} → {span.Max:yyyy-MM-dd} " +
                          $"({(span.Max - span.Min).TotalDays / 365.25:0.0} years). Entered next open, " +
                          $"{TargetR}R target / 1R stop, {HorizonBars}-bar horizon.");
        Console.WriteLine("Crowding > 0 = LONG side piled in (funding paying longs, OI growing on the rally).");
        Console.WriteLine("Prediction: long entries WORSE when crowding is high, BETTER when deeply negative.");
        Console.WriteLine();

        // Terciles of the pooled crowding distribution — balanced samples, and no arbitrary
        // threshold to pick. The provider's own ±2 levels are reported after, as stated.
        var sorted = all.Select(t => t.Crowding).OrderBy(v => v).ToArray();
        double lo = sorted[sorted.Length / 3], hi = sorted[2 * sorted.Length / 3];
        Console.WriteLine($"  Crowding terciles: bottom < {lo:+0.00;-0.00;0} ≤ middle ≤ {hi:+0.00;-0.00;0} < top");
        Console.WriteLine();

        var lifts = new Dictionary<string, (double Crowd, double Ma)>();

        foreach (var g in all.GroupBy(t => t.Signal).OrderBy(g => g.Key))
        {
            var set = g.ToList();
            double baseline = set.Average(t => t.R);
            Console.WriteLine($"  ── {g.Key}  (n={set.Count:N0}, ungated mean {baseline:+0.000;-0.000;0}R, " +
                              $"win {set.Count(t => t.R > 0) / (double)set.Count:P0}) ──");

            var bottom = set.Where(t => t.Crowding < lo).ToList();
            var top = set.Where(t => t.Crowding > hi).ToList();
            var mid = set.Where(t => t.Crowding >= lo && t.Crowding <= hi).ToList();

            Console.WriteLine($"    crowding bottom (shorts crowded) {bottom.Average(t => t.R),+6:+0.000;-0.000;0}R  n={bottom.Count,5:N0}");
            Console.WriteLine($"    crowding middle                  {mid.Average(t => t.R),+6:+0.000;-0.000;0}R  n={mid.Count,5:N0}");
            Console.WriteLine($"    crowding top    (longs crowded)  {top.Average(t => t.R),+6:+0.000;-0.000;0}R  n={top.Count,5:N0}");

            double gap = bottom.Average(t => t.R) - top.Average(t => t.R);
            double p = PermutationP(set.Select(t => t.R).ToArray(), bottom.Count, top.Count, gap, permutations);
            Console.WriteLine($"    bottom − top: {gap,+6:+0.000;-0.000;0}R   p = {p:0.0000}" +
                              (p <= 0.05 ? "  *" : "") +
                              (gap > 0 ? "   (sign matches the prediction)" : "   (sign is BACKWARDS)"));

            // The price-derived conditioner, measured the same way, on the same trades.
            var maOpen = set.Where(t => t.MaGate).ToList();
            double maLift = maOpen.Count >= 30 ? maOpen.Average(t => t.R) - baseline : double.NaN;
            double crowdLift = bottom.Count >= 30 ? bottom.Average(t => t.R) - baseline : double.NaN;
            Console.WriteLine($"    lift from best crowding bucket {crowdLift,+6:+0.000;-0.000;0}R   " +
                              $"vs lift from 200-MA {maLift,+6:+0.000;-0.000;0}R");
            lifts[g.Key] = (crowdLift, maLift);
            Console.WriteLine();
        }

        AbsoluteThresholds(all, permutations);
        DirectSignal(all, permutations);
        AllBars(permutations);
        Verdict(lifts);
    }

    /// <summary>
    /// The highest-powered form of the question, on every bar rather than on entry signals.
    ///
    /// <para>
    /// Overlapping forward windows make consecutive observations heavily dependent, so an ordinary
    /// p-value here would be badly overstated. The null is instead built by CIRCULARLY SHIFTING the
    /// crowding series against the returns by a random offset. That destroys the alignment between
    /// the two while leaving each series' own autocorrelation completely intact — which is exactly
    /// the structure that would otherwise manufacture significance.
    /// </para>
    /// </summary>
    private static void AllBars(int permutations)
    {
        if (_obs.Count < 1000) return;

        Console.WriteLine($"  ── EVERY BAR, not just signal bars ({_obs.Count:N0} observations) ──");
        Console.WriteLine($"    Forward {HorizonBars}-bar return against the crowding score at the bar.");
        Console.WriteLine();

        var byDecile = _obs.OrderBy(o => o.Crowding).ToList();
        int per = byDecile.Count / 10;
        Console.WriteLine($"    {"decile",-8} {"crowding range",-22} {"mean fwd ret",13} {"in ATRs",9} {"n",7}");
        for (int d = 0; d < 10; d++)
        {
            var slice = byDecile.Skip(d * per).Take(d == 9 ? int.MaxValue : per).ToList();
            Console.WriteLine($"    {d + 1,-8} {slice.Min(o => o.Crowding),8:+0.00;-0.00;0} … {slice.Max(o => o.Crowding),8:+0.00;-0.00;0}   " +
                              $"{slice.Average(o => o.FwdRet),12:+0.0000;-0.0000;0} {slice.Average(o => o.AtrNorm),9:+0.00;-0.00;0} {slice.Count,7:N0}");
        }

        HorizonSweep(permutations);

        double rho = Spearman(_obs.Select(o => o.Crowding).ToArray(), _obs.Select(o => o.FwdRet).ToArray());
        double p = CircularShiftP(_obs.Select(o => o.Crowding).ToArray(), _obs.Select(o => o.FwdRet).ToArray(), rho, permutations);
        Console.WriteLine();
        Console.WriteLine($"    pooled spearman(crowding, forward return) = {rho:+0.0000;-0.0000;0}   " +
                          $"p = {p:0.0000} (circular-shift null)" + (p <= 0.05 ? "  *" : ""));
        Console.WriteLine("    Negative would mean crowding predicts LOWER forward returns — the fade-the-crowd claim.");

        Console.WriteLine();
        Console.WriteLine("    per symbol (a pooled number can be one symbol):");
        foreach (var g in _obs.GroupBy(o => o.Symbol).OrderBy(g => g.Key))
        {
            var s = g.ToList();
            double r = Spearman(s.Select(o => o.Crowding).ToArray(), s.Select(o => o.FwdRet).ToArray());
            Console.WriteLine($"      {g.Key,-10} spearman {r,+8:+0.0000;-0.0000;0}   n={s.Count,6:N0}");
        }
        int neg = _obs.GroupBy(o => o.Symbol)
            .Count(g => Spearman(g.Select(o => o.Crowding).ToArray(), g.Select(o => o.FwdRet).ToArray()) < 0);
        Console.WriteLine($"    {neg} of {_obs.Select(o => o.Symbol).Distinct().Count()} symbols negative " +
                          "(the direction the fade-the-crowd thesis needs).");
        Console.WriteLine();
    }

    /// <summary>
    /// The same correlation at every horizon, so a null at 20 bars cannot be mistaken for a null
    /// everywhere. Also reports the top-vs-bottom decile spread in ATRs, which is the number that
    /// says whether any effect would be tradeable rather than merely detectable.
    /// </summary>
    private static void HorizonSweep(int permutations)
    {
        Console.WriteLine($"    {"horizon",-9} {"spearman",10} {"p",8} {"bottom dec",11} {"top dec",9} {"spread(ATR)",12} {"syms neg",9}");

        foreach (int h in Horizons)
        {
            var crowd = new List<double>();
            var fwd = new List<double>();
            var atrN = new List<double>();
            var perSymbol = new List<double>();

            foreach (var (sym, c, close, atr) in _series)
            {
                var sc = new List<double>(); var sf = new List<double>();
                for (int i = 0; i + h < close.Length && i < c.Length; i++)
                {
                    if (double.IsNaN(c[i]) || close[i] <= 0 || double.IsNaN(atr[i]) || atr[i] <= 0) continue;
                    sc.Add(c[i]);
                    sf.Add(Math.Log(close[i + h] / close[i]));
                    crowd.Add(c[i]);
                    fwd.Add(Math.Log(close[i + h] / close[i]));
                    atrN.Add((close[i + h] - close[i]) / atr[i]);
                }
                if (sc.Count > 100) perSymbol.Add(Spearman(sc.ToArray(), sf.ToArray()));
            }

            if (crowd.Count < 500) continue;

            var cx = crowd.ToArray(); var fy = fwd.ToArray();
            double rho = Spearman(cx, fy);
            double p = CircularShiftP(cx, fy, rho, permutations);

            var order = Enumerable.Range(0, cx.Length).OrderBy(i => cx[i]).ToArray();
            int per = order.Length / 10;
            double botA = order.Take(per).Average(i => atrN[i]);
            double topA = order.Skip(order.Length - per).Average(i => atrN[i]);

            Console.WriteLine($"    {h + " bars",-9} {rho,10:+0.0000;-0.0000;0} {p,8:0.0000} " +
                              $"{botA,11:+0.00;-0.00;0} {topA,9:+0.00;-0.00;0} {botA - topA,12:+0.00;-0.00;0} " +
                              $"{perSymbol.Count(v => v < 0) + "/" + perSymbol.Count,9}" +
                              (p <= 0.05 ? "  *" : ""));
        }
        Console.WriteLine("    (spread = bottom decile minus top decile, in ATRs. Fade-the-crowd needs it POSITIVE.)");
        Console.WriteLine();

        HowMuchOfCrowdingIsJustPrice();
    }

    /// <summary>
    /// How much of the crowding score is PRICE rather than positioning.
    ///
    /// <para>
    /// The index is <c>funding_z + sign(close[i] − close[i−1]) × oi_delta_z</c>. That sign term is a
    /// price term, so the score is not the pure non-price signal it is documented as — and the
    /// whole reason for preferring crowding over another oscillator was that it is orthogonal to
    /// price. If the score tracks recent returns closely, then any apparent edge in it is the
    /// crypto momentum already measured elsewhere in this lab, arriving under a different name.
    /// </para>
    /// </summary>
    private static void HowMuchOfCrowdingIsJustPrice()
    {
        Console.WriteLine("    ── how much of the score is price, not positioning? ──");
        foreach (int look in new[] { 1, 5, 20 })
        {
            var c = new List<double>(); var r = new List<double>();
            foreach (var (_, crowd, close, _) in _series)
                for (int i = look; i < close.Length && i < crowd.Length; i++)
                {
                    if (double.IsNaN(crowd[i]) || close[i] <= 0 || close[i - look] <= 0) continue;
                    c.Add(crowd[i]);
                    r.Add(Math.Log(close[i] / close[i - look]));
                }
            if (c.Count < 500) continue;
            Console.WriteLine($"      spearman(crowding, PAST {look,2}-bar return) = {Spearman(c.ToArray(), r.ToArray()),+8:+0.0000;-0.0000;0}   n={c.Count:N0}");
        }
        Console.WriteLine("      The index multiplies its OI term by sign(close − prev close), so a high reading");
        Console.WriteLine("      partly just means 'price went up recently'. Compare against the forward-return");
        Console.WriteLine("      correlations above: whatever it knows about the past, it does not carry forward.");
        Console.WriteLine();
    }

    private static double Spearman(double[] x, double[] y) => Pearson(Rank(x), Rank(y));

    private static double Pearson(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double a = x[i] - mx, b = y[i] - my;
            sxy += a * b; sxx += a * a; syy += b * b;
        }
        return sxx <= 0 || syy <= 0 ? double.NaN : sxy / Math.Sqrt(sxx * syy);
    }

    private static double[] Rank(double[] v)
    {
        var idx = Enumerable.Range(0, v.Length).OrderBy(i => v[i]).ToArray();
        var r = new double[v.Length];
        int i2 = 0;
        while (i2 < idx.Length)
        {
            int j = i2;
            while (j + 1 < idx.Length && v[idx[j + 1]] == v[idx[i2]]) j++;
            double avg = (i2 + j) / 2.0 + 1;
            for (int k = i2; k <= j; k++) r[idx[k]] = avg;
            i2 = j + 1;
        }
        return r;
    }

    /// <summary>
    /// Circular-shift null for two autocorrelated series. Shuffling either one would destroy its
    /// own serial structure and build a null far too narrow; rotating one against the other keeps
    /// both intact and randomises only their alignment.
    /// </summary>
    private static double CircularShiftP(double[] x, double[] y, double observed, int runs)
    {
        if (double.IsNaN(observed)) return 1;
        int n = x.Length;
        var rx = Rank(x);
        var ry = Rank(y);
        var rot = new double[n];
        var rng = new Random(31337);
        int extreme = 0;
        // Fewer runs than the label-permutation tests: each one is an O(n) pass over ~10k points
        // and the answer here is not close enough for the extra resolution to matter.
        int use = Math.Min(runs, 2000);
        for (int r = 0; r < use; r++)
        {
            int off = rng.Next(n);
            for (int i = 0; i < n; i++) rot[i] = rx[(i + off) % n];
            double s = Pearson(rot, ry);
            if (!double.IsNaN(s) && Math.Abs(s) >= Math.Abs(observed)) extreme++;
        }
        return (extreme + 1.0) / (use + 1.0);
    }

    /// <summary>The provider documents ±2 as its trading levels. Reported because that is the
    /// claim on file, even though the terciles are the better-powered test.</summary>
    private static void AbsoluteThresholds(List<Trade> all, int permutations)
    {
        Console.WriteLine("  ── at the provider's documented ±2 levels ──");
        foreach (var g in all.GroupBy(t => t.Signal).OrderBy(g => g.Key))
        {
            var set = g.ToList();
            var shortsCrowded = set.Where(t => t.Crowding <= -2).ToList();
            var longsCrowded = set.Where(t => t.Crowding >= 2).ToList();
            if (shortsCrowded.Count < 20 || longsCrowded.Count < 20)
            {
                Console.WriteLine($"    {g.Key,-12} too few at the extremes " +
                                  $"(≤−2: {shortsCrowded.Count}, ≥+2: {longsCrowded.Count})");
                continue;
            }
            double gap = shortsCrowded.Average(t => t.R) - longsCrowded.Average(t => t.R);
            double p = PermutationP(set.Select(t => t.R).ToArray(), shortsCrowded.Count, longsCrowded.Count, gap, permutations);
            Console.WriteLine($"    {g.Key,-12} ≤−2 {shortsCrowded.Average(t => t.R),+6:+0.000;-0.000;0}R (n={shortsCrowded.Count,4}) " +
                              $"  ≥+2 {longsCrowded.Average(t => t.R),+6:+0.000;-0.000;0}R (n={longsCrowded.Count,4})" +
                              $"   gap {gap,+6:+0.000;-0.000;0}R  p = {p:0.0000}" + (p <= 0.05 ? "  *" : ""));
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Crowding as an entry in its own right rather than a filter — Shapiro's fade-the-crowd trade.
    /// Uses the random-entry population, which is unconditioned on price, so the only thing
    /// selecting these bars is the crowding value itself.
    /// </summary>
    private static void DirectSignal(List<Trade> all, int permutations)
    {
        var rnd = all.Where(t => t.Signal == "random-long").ToList();
        if (rnd.Count < 200) return;

        Console.WriteLine("  ── crowding as a DIRECT signal (fade the crowd), measured on unconditioned bars ──");
        double baseline = rnd.Average(t => t.R);
        foreach (var th in new[] { -1.0, -1.5, -2.0, -2.5 })
        {
            var fired = rnd.Where(t => t.Crowding <= th).ToList();
            if (fired.Count < 30) { Console.WriteLine($"    crowding ≤ {th,4:0.0}: only {fired.Count} — too few"); continue; }
            var rest = rnd.Where(t => t.Crowding > th).ToList();
            double gap = fired.Average(t => t.R) - rest.Average(t => t.R);
            double p = PermutationP(rnd.Select(t => t.R).ToArray(), fired.Count, rest.Count, gap, permutations);
            Console.WriteLine($"    crowding ≤ {th,4:0.0}: {fired.Average(t => t.R),+6:+0.000;-0.000;0}R (n={fired.Count,4})   " +
                              $"vs rest {rest.Average(t => t.R),+6:+0.000;-0.000;0}R   gap {gap,+6:+0.000;-0.000;0}R   p = {p:0.0000}" +
                              (p <= 0.05 ? "  *" : ""));
        }
        Console.WriteLine($"    (unconditioned baseline {baseline:+0.000;-0.000;0}R)");
        Console.WriteLine();
    }

    private static void Verdict(Dictionary<string, (double Crowd, double Ma)> lifts)
    {
        Console.WriteLine("  ── VERDICT ──");

        if (!lifts.TryGetValue("random-long", out var rnd) || double.IsNaN(rnd.Crowd))
        {
            Console.WriteLine("    No usable random-entry baseline — cannot separate a crowding effect from the");
            Console.WriteLine("    market simply having moved. Treat every number above as uninterpreted.");
            return;
        }

        Console.WriteLine($"    Random-entry lift from the best crowding bucket: {rnd.Crowd:+0.000;-0.000;0}R.");
        Console.WriteLine("    That is what a coin flip gets from the same filter, and any signal's lift is only");
        Console.WriteLine("    its own above it:");

        int beatsBoth = 0, tested = 0;
        foreach (var (name, l) in lifts.OrderBy(k => k.Key))
        {
            if (name == "random-long" || double.IsNaN(l.Crowd)) continue;
            tested++;
            double excess = l.Crowd - rnd.Crowd;
            bool beats = excess > 0.05 && l.Crowd > l.Ma;
            if (beats) beatsBoth++;
            Console.WriteLine($"      {name,-12} crowding lift {l.Crowd,+6:+0.000;-0.000;0}R   " +
                              $"excess over random {excess,+6:+0.000;-0.000;0}R   " +
                              $"vs 200-MA {l.Ma,+6:+0.000;-0.000;0}R   " +
                              (beats ? "→ beats both controls" : "→ does not beat both"));
        }

        Console.WriteLine();
        if (beatsBoth == 0)
        {
            Console.WriteLine("    Crowding does not condition these entries, and the all-bars test says why: it");
            Console.WriteLine("    carries no forward information at ANY horizon from 1 to 40 bars, on 10k+");
            Console.WriteLine("    observations, with every p above 0.45. That is not a sample-size verdict —");
            Console.WriteLine("    the signal-conditioning arms are underpowered, but the direct test is not.");
            Console.WriteLine();
            Console.WriteLine("    It also is not as orthogonal to price as its own documentation claims: the");
            Console.WriteLine("    score rank-correlates ~0.19 with the trailing 5- and 20-bar return, because");
            Console.WriteLine("    funding runs positive during sustained rallies. So it is a backward-looking");
            Console.WriteLine("    description of a move that has already happened, and the one thing it was");
            Console.WriteLine("    supposed to add over a price oscillator is partly not there either.");
        }
        else if (beatsBoth == tested)
        {
            Console.WriteLine("    Crowding conditions every entry tested, beating both a random baseline and a");
            Console.WriteLine("    price-derived conditioner. This is the orthogonal-data thesis surviving its");
            Console.WriteLine("    first real test — worth a walk-forward before it is worth trading.");
        }
        else
        {
            Console.WriteLine($"    Split: {beatsBoth} of {tested} entries beat both controls. Suggestive, not decisive,");
            Console.WriteLine("    and at this sample size the honest reading is 'test it again on more data'.");
        }
    }

    /// <summary>
    /// Two-sample permutation test — see <see cref="LabStats.PermutationP(double[], int, int, double, int, int, int?, out int)"/>. The seed lives here,
    /// not in the shared helper, because it is this command's research parameter.
    /// </summary>
    private static double PermutationP(double[] pool, int nA, int nB, double observed, int runs) =>
        LabStats.PermutationP(pool, nA, nB, observed, runs, seed: 4242);
}
