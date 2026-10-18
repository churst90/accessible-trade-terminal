using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab.CycleSchool;

/// <summary>One daily (or weekly) bar, by trading day.</summary>
public readonly record struct Bar(DateOnly Day, double Open, double High, double Low, double Close);

/// <summary>A reference low (prereg 3.3): the low bar and the bar that confirmed it. It may be
/// used by any rule only from <see cref="Confirm"/> on.</summary>
public readonly record struct RefLow(int Low, int Confirm);

/// <summary>
/// The pure building blocks of the cycle-school study (<c>docs/CYCLE_SCHOOL_PREREG.md</c>), kept
/// free of file access so the tests can pin each causal property on synthetic bars.
///
/// <para><b>The trap this whole file is built around</b> (prereg section 2): a "cycle low" picked on
/// a finished chart is spaced the claimed way because of how it was picked. So the only lows any
/// rule here may use are reference lows from one fixed rule, and only from the bar that confirms
/// them (<see cref="ReferenceLows"/>, <see cref="KnownLowsAt"/>); weekly inputs use completed weeks
/// only (<see cref="Weekly"/>); every percentile is expanding (<see cref="ExpandingPct"/>).</para>
/// </summary>
public static class CycleSchoolCore
{
    // ── Indicators ───────────────────────────────────────────────────────────

    /// <summary>Wilder's ATR. TR(0) = high − low; the first ATR (bar n−1) is the mean of the first n
    /// true ranges; then ATR = (ATR·(n−1) + TR)/n. NaN before bar n−1.</summary>
    public static double[] Atr(IReadOnlyList<Bar> b, int n = 14)
    {
        var a = Nan(b.Count);
        if (b.Count < n) return a;
        double sum = 0;
        for (int i = 0; i < b.Count; i++)
        {
            double tr = TrueRange(b, i);
            if (i < n) { sum += tr; if (i == n - 1) a[i] = sum / n; continue; }
            a[i] = (a[i - 1] * (n - 1) + tr) / n;
        }
        return a;
    }

    public static double TrueRange(IReadOnlyList<Bar> b, int i) => i == 0
        ? b[i].High - b[i].Low
        : Math.Max(b[i].High - b[i].Low, Math.Max(Math.Abs(b[i].High - b[i - 1].Close), Math.Abs(b[i].Low - b[i - 1].Close)));

    public static double[] Sma(IReadOnlyList<double> x, int n)
    {
        var s = Nan(x.Count);
        double sum = 0;
        for (int i = 0; i < x.Count; i++)
        {
            sum += x[i];
            if (i >= n) sum -= x[i - n];
            if (i >= n - 1) s[i] = sum / n;
        }
        return s;
    }

    /// <summary>Wilder's RSI on closes. NaN before bar n.</summary>
    public static double[] Rsi(IReadOnlyList<double> c, int n = 14)
    {
        var r = Nan(c.Count);
        if (c.Count <= n) return r;
        double g = 0, l = 0;
        for (int i = 1; i <= n; i++)
        {
            double d = c[i] - c[i - 1];
            if (d > 0) g += d; else l -= d;
        }
        g /= n; l /= n;
        r[n] = l == 0 ? 100 : 100 - 100 / (1 + g / l);
        for (int i = n + 1; i < c.Count; i++)
        {
            double d = c[i] - c[i - 1];
            g = (g * (n - 1) + Math.Max(d, 0)) / n;
            l = (l * (n - 1) + Math.Max(-d, 0)) / n;
            r[i] = l == 0 ? 100 : 100 - 100 / (1 + g / l);
        }
        return r;
    }

    /// <summary>DSS Bressert from the app's own provider (prereg 4.1: "from DssBressertProvider").</summary>
    public static (double[] Dss, double[] Trigger) Dss(IReadOnlyList<Bar> b, int p = 10, int e = 9, int t = 5)
    {
        var data = b.Select(x => new Ohlcv(x.Day.ToDateTime(TimeOnly.MinValue), x.Open, x.High, x.Low, x.Close, 0)).ToArray();
        var buf = new Buffer(data.Length);
        new DssBressertProvider().Calculate(DssBressertProvider.Code, data,
            new Dictionary<string, object>
            {
                [DssBressertProvider.ParamStoch] = p, [DssBressertProvider.ParamEma] = e, [DssBressertProvider.ParamTrigger] = t,
            }, buf);
        return (buf.Get(DssBressertProvider.CompDss), buf.Get(DssBressertProvider.CompTrigger));
    }

    private sealed class Buffer : IIndicatorResultBuffer
    {
        private readonly int _n;
        private readonly Dictionary<string, double[]> _d = new();
        public Buffer(int n) => _n = n;
        public Span<double> GetComponentSpan(string name)
        {
            if (!_d.TryGetValue(name, out var a)) _d[name] = a = Nan(_n);
            return a;
        }
        public void SetValue(string name, int i, double v) => GetComponentSpan(name)[i] = v;
        public void WriteZoneBands(string code, List<ZoneBandConfig> z) { }
        public IReadOnlyList<ZoneBandConfig> ReadZoneBands(string code) => Array.Empty<ZoneBandConfig>();
        public double[] Get(string name) => _d.TryGetValue(name, out var a) ? a : Nan(_n);
    }

    public static double[] Nan(int n)
    {
        var a = new double[n];
        Array.Fill(a, double.NaN);
        return a;
    }

    // ── Weekly bars from daily bars (prereg 3.1) ─────────────────────────────

    /// <summary>A completed week: its bar, the index of its last daily bar, and the first daily bar
    /// on which it may be used.</summary>
    public readonly record struct Week(Bar Bar, int FirstDaily, int LastDaily, int UsableFrom);

    /// <summary>
    /// ISO weeks (Monday to Sunday). Crypto weeks end on Sunday; equity weeks on the week's last
    /// trading day. A week is usable from its last daily bar when that bar is the calendar end
    /// (Sunday for crypto, Friday for equities); otherwise (a holiday Friday, or the last, still
    /// open week on disk) only from the first bar of the next week. That is never earlier than the
    /// prereg allows and never depends on bars after the one being decided: it is the same on a
    /// truncated history, which is what <c>CycleSchoolCausalityTests</c> pins.
    /// </summary>
    public static List<Week> Weekly(IReadOnlyList<Bar> daily, bool crypto)
    {
        var weeks = new List<Week>();
        int start = 0;
        for (int i = 0; i < daily.Count; i++)
        {
            bool last = i == daily.Count - 1 || WeekKey(daily[i + 1].Day) != WeekKey(daily[i].Day);
            if (!last) continue;
            double hi = double.MinValue, lo = double.MaxValue;
            for (int k = start; k <= i; k++) { hi = Math.Max(hi, daily[k].High); lo = Math.Min(lo, daily[k].Low); }
            var bar = new Bar(daily[i].Day, daily[start].Open, hi, lo, daily[i].Close);
            var endDow = crypto ? DayOfWeek.Sunday : DayOfWeek.Friday;
            int usable = daily[i].Day.DayOfWeek == endDow ? i : (i + 1 < daily.Count ? i + 1 : int.MaxValue);
            weeks.Add(new Week(bar, start, i, usable));
            start = i + 1;
        }
        return weeks;
    }

    private static int WeekKey(DateOnly d)
    {
        var dt = d.ToDateTime(TimeOnly.MinValue);
        return System.Globalization.ISOWeek.GetYear(dt) * 100 + System.Globalization.ISOWeek.GetWeekOfYear(dt);
    }

    /// <summary>For each daily bar, the index of the newest week usable on it (−1 if none).</summary>
    public static int[] WeekAt(IReadOnlyList<Week> weeks, int nDaily)
    {
        var at = new int[nDaily];
        Array.Fill(at, -1);
        int w = -1;
        for (int i = 0; i < nDaily; i++)
        {
            while (w + 1 < weeks.Count && weeks[w + 1].UsableFrom <= i) w++;
            at[i] = w;
        }
        return at;
    }

    // ── The expanding percentile (design note section 3) ─────────────────────

    /// <summary>
    /// pct(i) = 100 × (number of defined x(k), k ≤ i, below x(i) + half the number equal) ÷ (number
    /// of defined values up to i). NaN where x(i) is NaN or fewer than <paramref name="min"/> values
    /// are defined. Never looks forward.
    /// </summary>
    public static double[] ExpandingPct(IReadOnlyList<double> x, int min)
    {
        var p = Nan(x.Count);
        var sorted = new List<double>();
        for (int i = 0; i < x.Count; i++)
        {
            double v = x[i];
            if (double.IsNaN(v)) continue;
            int lo = LowerBound(sorted, v);
            sorted.Insert(lo, v);
            int hi = UpperBound(sorted, v);
            if (sorted.Count < min) continue;
            double less = lo, equal = hi - lo; // includes itself
            p[i] = 100.0 * (less + 0.5 * equal) / sorted.Count;
        }
        return p;
    }

    private static int LowerBound(List<double> s, double v)
    {
        int lo = 0, hi = s.Count;
        while (lo < hi) { int m = (lo + hi) >> 1; if (s[m] < v) lo = m + 1; else hi = m; }
        return lo;
    }

    private static int UpperBound(List<double> s, double v)
    {
        int lo = 0, hi = s.Count;
        while (lo < hi) { int m = (lo + hi) >> 1; if (s[m] <= v) lo = m + 1; else hi = m; }
        return lo;
    }

    // ── Reference lows (prereg 3.3) ──────────────────────────────────────────

    /// <summary>
    /// Bar j is a reference low if its low is the lowest of bars j−<paramref name="lookback"/> to j,
    /// and, before any later bar has a low below it, some later bar closes at or above low(j) +
    /// <paramref name="mult"/> × ATR(14)(j). A bar whose low is broken on the same bar that would
    /// confirm it fails (the lower low is not "before"). <paramref name="unresolved"/> marks
    /// candidates whose fate is still open at the last bar: they cannot be scored either way.
    /// </summary>
    public static List<RefLow> ReferenceLows(IReadOnlyList<Bar> b, double[] atr, int lookback, double mult, out bool[] unresolved)
    {
        var list = new List<RefLow>();
        unresolved = new bool[b.Count];
        for (int j = lookback; j < b.Count; j++)
        {
            if (double.IsNaN(atr[j])) continue;
            double low = b[j].Low;
            bool lowest = true;
            for (int k = j - lookback; k < j && lowest; k++) if (b[k].Low < low) lowest = false;
            if (!lowest) continue;
            double target = low + mult * atr[j];
            int confirm = -1;
            bool broken = false;
            for (int k = j + 1; k < b.Count; k++)
            {
                if (b[k].Low < low) { broken = true; break; }
                if (b[k].Close >= target) { confirm = k; break; }
            }
            if (confirm >= 0) list.Add(new RefLow(j, confirm));
            else if (!broken) unresolved[j] = true;
        }
        return list;
    }

    /// <summary>The reference lows a rule may use on bar <paramref name="i"/>: confirmed on or before it.</summary>
    public static IEnumerable<RefLow> KnownLowsAt(IReadOnlyList<RefLow> lows, int i) => lows.Where(r => r.Confirm <= i);

    // ── Camel's checklist (prereg 4.1, 4.2) ──────────────────────────────────

    /// <summary>One checklist evaluation at bar i: the swing low it refers to and each item.</summary>
    public readonly record struct ChecklistBar(int I, int J, bool Win, bool Sma, bool Dss, bool Tl, bool Thr)
    {
        public int Count4 => (Sma ? 1 : 0) + (Dss ? 1 : 0) + (Tl ? 1 : 0) + (Thr ? 1 : 0);
    }

    /// <summary>Precomputed series the checklist reads, on one timeframe.</summary>
    public sealed class ChecklistInputs
    {
        public required IReadOnlyList<Bar> Bars;
        public required double[] Atr;
        public required double[] Sma10;
        public required double[] Dss;
        public required double[] Trigger;
        public required List<RefLow> Lows;        // reference lows of the window's degree
        public required double WindowLength;      // L in bars (or weeks)
        /// <summary>Maps a reference low's (daily) indices to this timeframe and back; identity for daily.</summary>
        public Func<RefLow, (int Low, int Confirm)> LowOnThisFrame = r => (r.Low, r.Confirm);
    }

    public static ChecklistInputs DailyChecklistInputs(IReadOnlyList<Bar> b, List<RefLow> lows, double windowLength)
    {
        var (dss, trig) = Dss(b);
        return new ChecklistInputs
        {
            Bars = b, Atr = Atr(b), Sma10 = Sma(b.Select(x => x.Close).ToList(), 10), Dss = dss, Trigger = trig,
            Lows = lows, WindowLength = windowLength,
        };
    }

    /// <summary>Item SW: the latest j in [i−10, i−2] whose low is the lowest low of bars j−10 to i; −1 if none.</summary>
    public static int SwingLow(IReadOnlyList<Bar> b, int i)
    {
        for (int j = i - 2; j >= i - 10; j--)
        {
            if (j - 10 < 0) break;
            double low = b[j].Low;
            bool ok = true;
            for (int k = j - 10; k <= i && ok; k++) if (k != j && b[k].Low < low) ok = false;
            if (ok) return j;
        }
        return -1;
    }

    /// <summary>Evaluates every item at bar i for the swing low <paramref name="j"/>.</summary>
    public static ChecklistBar Evaluate(ChecklistInputs x, int i, int j)
    {
        var b = x.Bars;
        // WIN: the most recent reference low whose low bar is before j and whose confirmation is on
        // or before i; bars from it to j at least 0.9·L. None yet → false.
        bool win = false;
        int best = -1;
        foreach (var r in x.Lows)
        {
            var (lo, cf) = x.LowOnThisFrame(r);
            if (lo < j && cf <= i && lo > best) best = lo;
        }
        if (best >= 0) win = j - best >= 0.9 * x.WindowLength;

        bool sma = !double.IsNaN(x.Sma10[i]) && b[i].Close > x.Sma10[i];

        bool dss = false;
        for (int k = Math.Max(1, j - 3); k <= i && !dss; k++)
        {
            double d = x.Dss[k], t = x.Trigger[k], d1 = x.Dss[k - 1], t1 = x.Trigger[k - 1];
            if (double.IsNaN(d) || double.IsNaN(t) || double.IsNaN(d1) || double.IsNaN(t1)) continue;
            if (d > t && d1 <= t1 && Math.Min(d1, t1) < 20) dss = true;
        }

        bool tl = TrendlineBreak(b, i, j);

        bool thr = false;
        for (int k = Math.Max(1, j); k <= i && !thr; k++)
        {
            var c = b[k];
            double range = c.High - c.Low;
            if (c.Close > c.Open && !double.IsNaN(x.Atr[k - 1]) && TrueRange(b, k) >= 1.5 * x.Atr[k - 1]
                && range > 0 && (c.Close - c.Low) / range >= 0.75) thr = true;
            var p = b[k - 1];
            if (c.Close > c.Open && p.Close < p.Open && c.Open <= p.Close && c.Close >= p.Open) thr = true;
        }
        return new ChecklistBar(i, j, win, sma, dss, tl, thr);
    }

    /// <summary>Item TL: pivot highs (the highest high of 3 bars either side) between j−120 and j−1;
    /// stepping back from the latest, the most recent consecutive pair H1 (earlier), H2 (later) with
    /// high(H2) &lt; high(H1); TL holds if close(i) is above their line extended to i.</summary>
    public static bool TrendlineBreak(IReadOnlyList<Bar> b, int i, int j)
    {
        var piv = new List<int>();
        for (int p = Math.Max(3, j - 120); p <= j - 1; p++)
        {
            if (p + 3 >= b.Count) break;
            double h = b[p].High;
            bool ok = true;
            for (int k = p - 3; k <= p + 3 && ok; k++) if (k != p && b[k].High > h) ok = false;
            if (ok) piv.Add(p);
        }
        for (int k = piv.Count - 1; k >= 1; k--)
        {
            int h2 = piv[k], h1 = piv[k - 1];
            if (b[h2].High < b[h1].High)
            {
                double slope = (b[h2].High - b[h1].High) / (h2 - h1);
                double line = b[h2].High + slope * (i - h2);
                return b[i].Close > line;
            }
        }
        return false;
    }

    /// <summary>The checklist's calls: the first bar i on which <paramref name="rule"/> holds for a
    /// given swing low j; one call per j (prereg 4.2). <paramref name="from"/> is the first bar at
    /// which every input is defined.</summary>
    public static List<ChecklistBar> ChecklistCalls(ChecklistInputs x, Func<ChecklistBar, bool> rule, int from = 0)
    {
        var calls = new List<ChecklistBar>();
        var done = new HashSet<int>();
        for (int i = Math.Max(from, 12); i < x.Bars.Count; i++)
        {
            int j = SwingLow(x.Bars, i);
            if (j < 0 || done.Contains(j)) continue;
            var e = Evaluate(x, i, j);
            if (!rule(e)) continue;
            calls.Add(e);
            done.Add(j);
        }
        return calls;
    }

    public static bool RulePrimary(ChecklistBar e) => e.Win && e.Count4 >= 3;
    public static bool RuleAnyOne(ChecklistBar e) => e.Win && e.Count4 >= 1;
    public static bool RulePivotDssTl(ChecklistBar e) => e.Win && e.Dss && e.Tl;
    public static bool RuleTwoOfFour(ChecklistBar e) => e.Win && e.Count4 >= 2;
    public static bool RuleNoWindow(ChecklistBar e) => e.Count4 >= 3;

    /// <summary>The N2 baseline for the checklist: every swing low j and the bars i ∈ [j+2, j+10]
    /// on which SW names it.</summary>
    public static Dictionary<int, List<int>> SwingEligible(IReadOnlyList<Bar> b, int from = 0)
    {
        var map = new Dictionary<int, List<int>>();
        for (int i = Math.Max(from, 12); i < b.Count; i++)
        {
            int j = SwingLow(b, i);
            if (j < 0) continue;
            if (!map.TryGetValue(j, out var l)) map[j] = l = new();
            l.Add(i);
        }
        return map;
    }

    // ── The capitulation score's events (design note 4.3, 4.4) ───────────────

    /// <summary>
    /// ALERT on bar i: pct(i) ≥ <paramref name="threshold"/>; a low is due (bars since the newest
    /// reference low KNOWN on i ≥ 0.75 × the median of the earlier known spacings, needing 4;
    /// treated as met with fewer), unless <paramref name="useDue"/> is false; and no alert in the
    /// previous 20 bars.
    /// </summary>
    public static List<int> Alerts(double[] pct, IReadOnlyList<RefLow> lows, double threshold, bool useDue)
    {
        var calls = new List<int>();
        int last = int.MinValue / 2;
        var byConfirm = lows.OrderBy(r => r.Confirm).ToList();
        var known = new List<int>(); // low bars of known lows, kept sorted
        int k = 0;
        for (int i = 0; i < pct.Length; i++)
        {
            while (k < byConfirm.Count && byConfirm[k].Confirm <= i)
            {
                int pos = known.BinarySearch(byConfirm[k].Low);
                known.Insert(pos < 0 ? ~pos : pos, byConfirm[k].Low);
                k++;
            }
            if (double.IsNaN(pct[i]) || pct[i] < threshold) continue;
            if (i - last <= 20) continue;
            if (useDue && !LowDue(known, i)) continue;
            calls.Add(i);
            last = i;
        }
        return calls;
    }

    /// <summary>The "low due" rule over the known low bars (sorted ascending).</summary>
    public static bool LowDue(List<int> knownLowBars, int i)
    {
        if (knownLowBars.Count < 5) return true; // fewer than 4 spacings: treated as met
        var sp = new List<double>();
        for (int a = 1; a < knownLowBars.Count; a++) sp.Add(knownLowBars[a] - knownLowBars[a - 1]);
        sp.Sort();
        double med = sp.Count % 2 == 1 ? sp[sp.Count / 2] : 0.5 * (sp[sp.Count / 2 - 1] + sp[sp.Count / 2]);
        return i - knownLowBars[^1] >= 0.75 * med;
    }

    public readonly record struct Confirmation(int I, int J, double Grade, int Cancelled);

    /// <summary>
    /// CONFIRMED on bar i (design note 4.4): anchor j = the lowest low among bars i−<paramref name="anchorBack"/>
    /// to i−1 (the latest on a tie); (1) low(j) is the lowest low of bars j−<paramref name="swingBack"/>
    /// to i; (2) the highest pct over j−5 to j is at least 90; (3) pct(i) is at least 15 below the
    /// highest pct over j−5 to i; (4) close(i) &gt; high(j); (5) first such bar for j. With
    /// <paramref name="pct"/> = null only conditions 1 and 4 are tested (the N2-eligible bars).
    /// </summary>
    public static List<Confirmation> Confirmations(IReadOnlyList<Bar> b, double[]? pct, int anchorBack = 10, int swingBack = 20, bool allEligible = false)
    {
        var res = new List<Confirmation>();
        var done = new HashSet<int>();
        for (int i = anchorBack; i < b.Count; i++)
        {
            int j = -1;
            for (int k = i - anchorBack; k <= i - 1; k++) if (j < 0 || b[k].Low <= b[j].Low) j = k;
            if (j - swingBack < 0) continue;
            if (!allEligible && done.Contains(j)) continue;
            double low = b[j].Low;
            bool c1 = true;
            for (int k = j - swingBack; k <= i && c1; k++) if (k != j && b[k].Low < low) c1 = false;
            if (!c1 || !(b[i].Close > b[j].High)) continue;
            double grade = double.NaN;
            if (pct != null)
            {
                double atLow = MaxPct(pct, j - 5, j), sinceLow = MaxPct(pct, j - 5, i);
                if (double.IsNaN(atLow) || atLow < 90) continue;
                if (double.IsNaN(pct[i]) || pct[i] > sinceLow - 15) continue;
                grade = atLow;
            }
            int cancelled = -1;
            for (int k = i + 1; k < b.Count; k++) if (b[k].Close < low) { cancelled = k; break; }
            res.Add(new Confirmation(i, j, grade, cancelled));
            done.Add(j);
        }
        return res;
    }

    private static double MaxPct(double[] p, int a, int z)
    {
        double m = double.NaN;
        for (int k = Math.Max(0, a); k <= z && k < p.Length; k++)
            if (!double.IsNaN(p[k]) && (double.IsNaN(m) || p[k] > m)) m = p[k];
        return m;
    }

    // ── Scoring (prereg 3.4) ─────────────────────────────────────────────────

    /// <summary>A hit window relative to the call bar t: a reference low j counts if
    /// t − <see cref="Before"/> ≤ j ≤ t + <see cref="After"/>.</summary>
    public readonly record struct HitWindow(int Before, int After);

    public readonly record struct Scored(int T, bool Hit, int Lag, int Lead, int LowBar);

    /// <summary>
    /// Scores calls against reference lows. A call is excluded (returns null) when its window runs
    /// past the last bar, or when a bar in its window is a candidate whose fate is still open at the
    /// last bar. For a hit, the low used for lag and lead is the one nearest the call (the earlier on
    /// a tie).
    /// </summary>
    public static Scored? Score(int t, HitWindow w, IReadOnlyList<RefLow> lows, bool[] unresolved, int nBars)
    {
        int a = t - w.Before, z = t + w.After;
        if (a < 0 || z >= nBars) return null;
        for (int k = a; k <= z; k++) if (unresolved[k]) return null;
        RefLow? best = null;
        foreach (var r in lows)
        {
            if (r.Low < a || r.Low > z) continue;
            if (best is null || Math.Abs(r.Low - t) < Math.Abs(best.Value.Low - t)) best = r;
        }
        return best is { } h ? new Scored(t, true, t - h.Low, h.Confirm - t, h.Low) : new Scored(t, false, 0, 0, -1);
    }

    // ── Statistics ───────────────────────────────────────────────────────────

    /// <summary>Holm–Bonferroni step-down: adjusted p-values, in the input order.</summary>
    public static double[] Holm(IReadOnlyList<double> p)
    {
        int m = p.Count;
        var idx = Enumerable.Range(0, m).Where(i => !double.IsNaN(p[i])).OrderBy(i => p[i]).ToList();
        var adj = Nan(m);
        double run = 0;
        int mm = idx.Count;
        for (int r = 0; r < mm; r++)
        {
            double v = Math.Min(1, (mm - r) * p[idx[r]]);
            run = Math.Max(run, v);
            adj[idx[r]] = run;
        }
        return adj;
    }

    /// <summary>The one RNG every random draw in the study uses: seeded from (test, asset, draw)
    /// through <see cref="StableSeed"/> (prereg 3.5).</summary>
    public static Random Rng(string test, string asset, int draw) =>
        new(StableSeed.From($"cycle-school|{test}|{asset}|{draw}"));

    /// <summary>
    /// N1: shifts a call series by one uniform offset in [60, L − 60] and wraps it within the era's
    /// scorable bars [<paramref name="a"/>, <paramref name="z"/>]. The number of calls and their
    /// spacing are kept; their link to price is not.
    /// </summary>
    public static List<int> CircularShift(IReadOnlyList<int> calls, int a, int z, Random rng)
    {
        int len = z - a + 1;
        if (len <= 121) return calls.ToList();
        int off = rng.Next(60, len - 60 + 1);
        return calls.Select(t => a + ((t - a + off) % len)).OrderBy(t => t).ToList();
    }

    /// <summary>The empirical quantile (linear interpolation between order statistics).</summary>
    public static double Quantile(IReadOnlyList<double> xs, double q)
    {
        var s = xs.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
        if (s.Count == 0) return double.NaN;
        double pos = q * (s.Count - 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return s[lo] + (s[hi] - s[lo]) * (pos - lo);
    }

    public static double Median(IReadOnlyList<double> xs) => Quantile(xs, 0.5);

    // ── Trades (prereg section 7) ────────────────────────────────────────────

    public readonly record struct Trade(int Signal, int Entry, int Exit, double EntryPx, double Stop, double ExitPx, double RGross, double RNet, string Why);

    public enum TradeStatus { Taken, Skipped, PastData }

    /// <summary>
    /// Enter at the open of i+1; initial stop low(j) − 0.25·ATR(i), and at least 0.5·ATR(i) below
    /// the entry. Skipped if the entry bar opens at or below low(j) − 0.25·ATR(i) (the stop before
    /// the floor: after the floor no open can be below it). Exit on the stop (at the stop, or at the
    /// open on a gap through it), else the close of the first bar at least 5 bars after entry
    /// closing below SMA(10), else the close of the 60th bar after entry. The stop wins a tie. R is
    /// from the initial risk; costs 2 × <paramref name="costPerSide"/> × entry ÷ risk. A trade whose
    /// 60-bar window runs past the last bar is not scored (PastData), like a call whose window does.
    /// </summary>
    public static (Trade? Trade, TradeStatus Status) CamelTrade(IReadOnlyList<Bar> b, double[] atr, double[] sma10, int i, double stopAnchorLow, double costPerSide = 0.0015)
    {
        int e = i + 1;
        if (e + 60 >= b.Count || double.IsNaN(atr[i])) return (null, TradeStatus.PastData);
        double entry = b[e].Open;
        double baseStop = stopAnchorLow - 0.25 * atr[i];
        if (entry <= baseStop) return (null, TradeStatus.Skipped);
        double stop = Math.Min(baseStop, entry - 0.5 * atr[i]);
        int last = e + 60;
        double risk = entry - stop;
        for (int k = e; k <= last; k++)
        {
            if (k > e && b[k].Open <= stop) return (Make(i, e, k, entry, stop, b[k].Open, risk, costPerSide, "stop-gap"), TradeStatus.Taken);
            if (b[k].Low <= stop) return (Make(i, e, k, entry, stop, stop, risk, costPerSide, "stop"), TradeStatus.Taken);
            if (k >= e + 5 && !double.IsNaN(sma10[k]) && b[k].Close < sma10[k]) return (Make(i, e, k, entry, stop, b[k].Close, risk, costPerSide, "sma10"), TradeStatus.Taken);
        }
        return (Make(i, e, last, entry, stop, b[last].Close, risk, costPerSide, "time"), TradeStatus.Taken);
    }

    /// <summary>The lab's standard harness (reference row): 1×ATR stop, 2R target, 20 bars.</summary>
    public static Trade? StandardTrade(IReadOnlyList<Bar> b, double[] atr, int i, double costPerSide = 0.0015)
    {
        int e = i + 1;
        if (e + 20 >= b.Count || double.IsNaN(atr[i])) return null;
        double entry = b[e].Open, risk = atr[i], stop = entry - risk, target = entry + 2 * risk;
        int last = e + 20;
        for (int k = e; k <= last; k++)
        {
            if (k > e && b[k].Open <= stop) return Make(i, e, k, entry, stop, b[k].Open, risk, costPerSide, "stop-gap");
            if (b[k].Low <= stop) return Make(i, e, k, entry, stop, stop, risk, costPerSide, "stop");
            if (k > e && b[k].Open >= target) return Make(i, e, k, entry, stop, b[k].Open, risk, costPerSide, "target-gap");
            if (b[k].High >= target) return Make(i, e, k, entry, stop, target, risk, costPerSide, "target");
        }
        return Make(i, e, last, entry, stop, b[last].Close, risk, costPerSide, "time");
    }

    private static Trade Make(int i, int e, int k, double entry, double stop, double exit, double risk, double cost, string why)
    {
        double gross = (exit - entry) / risk;
        return new Trade(i, e, k, entry, stop, exit, gross, gross - 2 * cost * entry / risk, why);
    }
}
