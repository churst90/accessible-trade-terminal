using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.StrategyLab;

/// <summary>Where a setup's wait ended (pre-registration section 2.4).</summary>
internal enum WaitStatus
{
    /// <summary>The wait found its signal bar.</summary>
    Confirmed,
    /// <summary>E1 only: a wave went as deep as the anchor (or deeper) first.</summary>
    Void,
    /// <summary>The window (30 bars, or one candle for E1h) ran out without a signal.</summary>
    Expired,
    /// <summary>The data ends before the wait could resolve. Never true on an eligible setup.</summary>
    Pending,
}

/// <summary>
/// One Buy dot followed forward (section 2.2 and 2.4). Every index is a bar on the entry (trigger)
/// timeframe. -1 means "not reached".
/// </summary>
internal sealed record Mc2Path
{
    public required int Dot;
    /// <summary>b(i): the most recent bear cross before the dot; -1 when there is none (anchor undefined).</summary>
    public required int AnchorStart;
    /// <summary>e: the first bear cross after the dot, within the 30-bar window; -1 otherwise.</summary>
    public int AnchorEnd = -1;
    /// <summary>T_A: the lowest WT2 from b(i) to e; NaN until e is known.</summary>
    public double AnchorTrough = double.NaN;
    public WaitStatus E1 = WaitStatus.Pending;
    /// <summary>j: the trigger bar (signal bar of E1).</summary>
    public int E1Bar = -1;
    public int VoidBar = -1;
    /// <summary>T(j), the trigger wave's trough.</summary>
    public double TriggerTrough = double.NaN;
    public WaitStatus E1h = WaitStatus.Pending;
    public int E1hBar = -1;
    public WaitStatus E1m = WaitStatus.Pending;
    public int E1mBar = -1;
}

/// <summary>Everything the round-2 grader reads from one series, computed once.</summary>
internal sealed class Mc2Series
{
    public required string Symbol;
    public required GraderSeriesConfig Cfg;
    public required List<Ohlcv> Bars;
    public required double[] Atr, Wt1, Wt2, Mf;
    public required bool[] BuyDot, BullX, BearX, BearDiv;
    public required HigherTf Htf;
    public required double[] HMf, HWt1, HRsi;
    public required bool[] HBuyDot;
    /// <summary>Context per HIGHER bar h (section 2.3), Undef where any input is undefined.</summary>
    public required int[] CtxH;
    /// <summary>Context per entry bar t: <c>CtxH[Htf.UsableAt[t]]</c>, the newest COMPLETED higher bar.</summary>
    public required int[] Ctx;
    /// <summary>Per entry bar: true when the higher timeframe printed a Buy dot on one of its last
    /// three completed bars (S6); false where fewer than three completed bars exist.</summary>
    public required bool[] HtfDotRecent;
}

/// <summary>
/// The pure parts of the round-2 Market Cipher setup grader (<c>docs/SETUP_GRADER_ROUND2_MC_PREREG.md</c>,
/// committed 60a94a0d before any result): the higher-timeframe context (V, A, B, C), the anchor wave,
/// the void rule, the trigger wave (E1) and the two secondary waits (E1h, E1m), the school's own stop,
/// and the gap-keeping perturbation. Everything here reads the bars up to and including the bar it
/// decides on and nothing after it; <c>SetupGraderMc2Tests</c> holds that to account by truncating the
/// series.
///
/// <para>
/// REUSED FROM ROUND 1, unchanged: completed higher-timeframe bars (<see cref="SetupGraderCore.Aggregate"/>,
/// whose <c>UsableAt</c> gives a lower bar only a higher bar whose period has ENDED), the standard R
/// harness, the cost, the ladder and the stop-and-rule exits, Holm. Not used: the app's own
/// higher-timeframe path and the engine's R.
/// </para>
/// </summary>
internal static class SetupGraderMc2Core
{
    public const int Undef = int.MinValue;
    /// <summary>Context states, in the pre-registration's order of precedence V, A, B, C.</summary>
    public const int CtxA = 0, CtxB = 1, CtxC = 2, CtxV = 3;
    public const int Window = 30;            // trigger window (free choice, frozen)
    public const int ThickBars = 104, ThickMin = 26;
    public const int HtfDotBars = 3;
    /// <summary>Section 2.6: bar i + 30 + 21 must exist.</summary>
    public const int EligibleAhead = Window + 21;

    public static string CtxName(int c) => c switch { CtxA => "A", CtxB => "B", CtxC => "C", CtxV => "V", _ => "undef" };

    // ── Building a series ────────────────────────────────────────────────────

    public static async Task<Mc2Series> BuildAsync(IIndicatorEngine engine, string symbol, List<Ohlcv> bars, GraderSeriesConfig cfg)
    {
        int n = bars.Count;
        var p = new Dictionary<string, object> { ["__symbol"] = symbol, ["__timeframe"] = cfg.Tf };
        var cb = await engine.CalculateAsync("CIPHER_B", bars, p, default);
        var htf = SetupGraderCore.Aggregate(bars, cfg.BucketOf, cfg.BucketLen, cfg.LowerTf, cfg.Calendar);
        int m = htf.Bars.Count;
        Dictionary<string, double[]>? hcb = null;
        if (m >= 10)
        {
            var hp = new Dictionary<string, object> { ["__symbol"] = symbol, ["__timeframe"] = cfg.HtfTf };
            hcb = await engine.CalculateAsync("CIPHER_B", htf.Bars, hp, default);
        }
        double[] Get(Dictionary<string, double[]>? d, string c, int len) =>
            d != null && d.TryGetValue(c, out var a) && a.Length == len ? a : Nan(len);
        bool[] Mark(double[] a) => a.Select(v => !double.IsNaN(v)).ToArray();

        var hMf = Get(hcb, CipherBProvider.CompMoneyFlowWave, m);
        var hWt1 = Get(hcb, CipherBProvider.CompWT1, m);
        var hRsi = Get(hcb, CipherBProvider.CompMcbRsi, m);
        var hDot = Mark(Get(hcb, CipherBProvider.CompBlue, m));
        var ctxH = ContextPerHigherBar(hMf, hWt1, hRsi);
        var ctx = new int[n];
        var recent = new bool[n];
        for (int t = 0; t < n; t++)
        {
            int h = htf.UsableAt[t];
            ctx[t] = h >= 0 ? ctxH[h] : Undef;
            recent[t] = h >= HtfDotBars - 1 && Enumerable.Range(h - HtfDotBars + 1, HtfDotBars).Any(k => hDot[k]);
        }
        return new Mc2Series
        {
            Symbol = symbol, Cfg = cfg, Bars = bars,
            Atr = IndicatorMath.Atr(bars.ToArray(), 14),
            Wt1 = Get(cb, CipherBProvider.CompWT1, n),
            Wt2 = Get(cb, CipherBProvider.CompWT2, n),
            Mf = Get(cb, CipherBProvider.CompMoneyFlowWave, n),
            BuyDot = Mark(Get(cb, CipherBProvider.CompBlue, n)),
            BullX = Mark(Get(cb, CipherBProvider.CompCrossBull, n)),
            BearX = Mark(Get(cb, CipherBProvider.CompCrossBear, n)),
            // Removed from Cipher B on 2026-10-31; the graders whose exit X4 reads it refuse to run.
            BearDiv = new bool[n],
            Htf = htf, HMf = hMf, HWt1 = hWt1, HRsi = hRsi, HBuyDot = hDot,
            CtxH = ctxH, Ctx = ctx, HtfDotRecent = recent,
        };
    }

    private static double[] Nan(int n) { var a = new double[n]; Array.Fill(a, double.NaN); return a; }

    // ── Section 2.3: the higher-timeframe context ───────────────────────────

    /// <summary>
    /// V: money flow, the fast wave and the RSI line each fell on each of the last two completed
    /// higher bars. A: not V, money flow above zero and at least the median |money flow| of the
    /// trailing 104 higher bars (h included, 26 defined values minimum). B: not V or A, money flow
    /// rising. C: the rest. Any undefined input makes the context undefined.
    /// </summary>
    public static int[] ContextPerHigherBar(double[] mf, double[] wt1, double[] rsi)
    {
        int m = mf.Length;
        var c = new int[m];
        var win = new List<double>(ThickBars);
        for (int h = 0; h < m; h++)
        {
            c[h] = Undef;
            if (h < 2) continue;
            bool def = true;
            for (int k = h - 2; k <= h; k++)
                if (double.IsNaN(mf[k]) || double.IsNaN(wt1[k]) || double.IsNaN(rsi[k])) def = false;
            if (!def) continue;
            win.Clear();
            for (int k = Math.Max(0, h - ThickBars + 1); k <= h; k++) if (!double.IsNaN(mf[k])) win.Add(Math.Abs(mf[k]));
            if (win.Count < ThickMin) continue;
            win.Sort();
            int q = win.Count;
            double median = q % 2 == 1 ? win[q / 2] : 0.5 * (win[q / 2 - 1] + win[q / 2]);
            bool down(double[] x) => x[h] < x[h - 1] && x[h - 1] < x[h - 2];
            if (down(mf) && down(wt1) && down(rsi)) c[h] = CtxV;
            else if (mf[h] > 0 && mf[h] >= median) c[h] = CtxA;
            else if (mf[h] > mf[h - 1]) c[h] = CtxB;
            else c[h] = CtxC;
        }
        return c;
    }

    // ── Sections 2.2 and 2.4: the anchor, the void rule and the waits ───────

    /// <summary>
    /// Follows the Buy dot at <paramref name="i"/> forward through bar min(i + 30, n - 1), where n is
    /// the length of the arrays: nothing at or beyond n is read, so a series cut at bar t gives the
    /// same answer for every event at or before t (a wait that has not resolved by the end of the
    /// data is <see cref="WaitStatus.Pending"/>).
    /// </summary>
    public static Mc2Path Follow(int i, double[] wt1, double[] wt2, double[] mf, bool[] bullX, bool[] bearX)
    {
        int n = wt2.Length;
        int last = Math.Min(i + Window, n - 1);
        int b = -1;
        for (int k = i - 1; k >= 0; k--) if (bearX[k]) { b = k; break; }
        var path = new Mc2Path { Dot = i, AnchorStart = b };

        // E1h: one candle for the cross to hold.
        if (i + 1 < n)
        {
            if (wt1[i + 1] >= wt2[i + 1]) { path.E1h = WaitStatus.Confirmed; path.E1hBar = i + 1; }
            else path.E1h = WaitStatus.Expired;
        }

        // E1m: the first bar from i to i + 30 with money flow above zero, and below or at zero the bar before.
        for (int k = i; k <= last; k++)
        {
            if (mf[k] > 0 && (k == i || mf[k - 1] <= 0)) { path.E1m = WaitStatus.Confirmed; path.E1mBar = k; break; }
        }
        if (path.E1m == WaitStatus.Pending && i + Window <= n - 1) path.E1m = WaitStatus.Expired;

        // E1: the trigger wave. Needs the anchor's start (an earlier bear cross).
        if (b < 0) { path.E1 = WaitStatus.Pending; return path; }
        int e = -1;
        for (int k = i + 1; k <= last; k++) if (bearX[k]) { e = k; break; }
        if (e < 0)
        {
            path.E1 = i + Window <= n - 1 ? WaitStatus.Expired : WaitStatus.Pending;
            return path;
        }
        double ta = double.PositiveInfinity;
        for (int k = b; k <= e; k++) ta = Math.Min(ta, wt2[k]);   // the bars after the dot belong to the anchor
        path.AnchorEnd = e; path.AnchorTrough = ta;
        int lastBear = e;
        for (int t = e + 1; t <= last; t++)
        {
            if (wt2[t] <= ta) { path.E1 = WaitStatus.Void; path.VoidBar = t; return path; }
            if (bearX[t]) { lastBear = t; continue; }
            if (!bullX[t]) continue;
            double trough = double.PositiveInfinity;
            for (int k = lastBear; k <= t; k++) trough = Math.Min(trough, wt2[k]);
            if (trough > ta && trough < 0 && mf[t] > mf[t - 1])
            {
                path.E1 = WaitStatus.Confirmed; path.E1Bar = t; path.TriggerTrough = trough;
                return path;
            }
        }
        path.E1 = i + Window <= n - 1 ? WaitStatus.Expired : WaitStatus.Pending;
        return path;
    }

    public static Mc2Path Follow(Mc2Series s, int i) => Follow(i, s.Wt1, s.Wt2, s.Mf, s.BullX, s.BearX);

    // ── Section 2.5: the school's own stop ──────────────────────────────────

    /// <summary>The lowest low from b(i) to the signal bar s, less 0.25 ATR(s); at least 0.5 ATR(s)
    /// below the entry (bar s + 1's open).</summary>
    public static double SchoolStop(IReadOnlyList<Ohlcv> bars, double[] atr, int anchorStart, int s)
    {
        double low = double.MaxValue;
        for (int k = anchorStart; k <= s; k++) low = Math.Min(low, bars[k].Low);
        double a = atr[s];
        double stop = low - SetupGraderCore.StopBuffer * a;
        double entry = bars[s + 1].Open;
        if (entry - stop < SetupGraderCore.StopFloor * a) stop = entry - SetupGraderCore.StopFloor * a;
        return stop;
    }

    // ── Section 6.4: the gap-keeping perturbation ───────────────────────────

    /// <summary>
    /// Noise injection that keeps each bar's own opening gap. The closes follow exactly the path
    /// <c>GateCommand.Perturb</c> gives them (each log return plus alpha x sigma x a Gaussian draw,
    /// sigma the series' own close-to-close volatility, the same RNG sequence). Unlike that function,
    /// which sets every open equal to its close, here noisy open = previous noisy close x (open /
    /// previous close), so "next open" stays an open and Heikin-Ashi bodies keep their shape. The
    /// high and low carry the bar's high/close and low/close ratios and are widened, if needed, to
    /// contain the open and the close. Bar 0 is unchanged. <c>GateCommand.Perturb</c> is not edited.
    /// </summary>
    public static List<Ohlcv> PerturbKeepGap(List<Ohlcv> bars, double alpha, int seed)
    {
        double sum = 0, sumSq = 0; int cnt = 0;
        var rets = new double[bars.Count];
        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i].Close <= 0 || bars[i - 1].Close <= 0) continue;
            rets[i] = Math.Log(bars[i].Close / bars[i - 1].Close);
            sum += rets[i]; sumSq += rets[i] * rets[i]; cnt++;
        }
        if (cnt < 2) return bars;
        double mean = sum / cnt;
        double sigma = Math.Sqrt(Math.Max(1e-12, sumSq / cnt - mean * mean));

        var rng = new Random(seed);
        var outp = new List<Ohlcv>(bars.Count) { bars[0] };
        double px = bars[0].Close;
        for (int i = 1; i < bars.Count; i++)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double prevNoisy = px;
            px = Math.Max(1e-9, px * Math.Exp(rets[i] + alpha * sigma * g));
            double gap = bars[i - 1].Close > 0 && bars[i].Open > 0 ? bars[i].Open / bars[i - 1].Close : 1.0;
            double open = prevNoisy * gap;
            double refC = bars[i].Close > 0 ? bars[i].Close : px;
            double hi = Math.Max(px * bars[i].High / refC, Math.Max(open, px));
            double lo = Math.Min(px * bars[i].Low / refC, Math.Min(open, px));
            outp.Add(new Ohlcv
            {
                Date = bars[i].Date, Open = open, High = hi, Low = lo, Close = px, Volume = bars[i].Volume,
            });
        }
        return outp;
    }

    // ── Section 6.5: the cluster sign-flip ──────────────────────────────────

    /// <summary>
    /// Cluster ids for setups on one asset, in dot order: a setup whose dot is within
    /// <paramref name="gap"/> bars of the previous setup's dot joins its cluster.
    /// </summary>
    public static int[] Clusters(IReadOnlyList<int> dots, int gap = 50)
    {
        var c = new int[dots.Count];
        int id = -1;
        for (int k = 0; k < dots.Count; k++)
        {
            if (k == 0 || dots[k] - dots[k - 1] > gap) id++;
            c[k] = id;
        }
        return c;
    }

    /// <summary>
    /// The sign-flip null over cluster sums: each cluster's summed per-setup difference is flipped
    /// at random, <paramref name="draws"/> times, and the mean over setups recomputed. One-sided p =
    /// (1 + draws at or above the observed) / (1 + draws); two-sided uses |null| against |observed|.
    /// </summary>
    public static double SignFlipP(IReadOnlyList<double> clusterSums, int nSetups, double observed, bool twoSided, int draws, Random rng)
    {
        int ge = 0;
        for (int d = 0; d < draws; d++)
        {
            double s = 0;
            foreach (double c in clusterSums) s += rng.Next(2) == 0 ? c : -c;
            double v = s / nSetups;
            if (twoSided ? Math.Abs(v) >= Math.Abs(observed) - 1e-12 : v >= observed - 1e-12) ge++;
        }
        return (1.0 + ge) / (1.0 + draws);
    }
}
