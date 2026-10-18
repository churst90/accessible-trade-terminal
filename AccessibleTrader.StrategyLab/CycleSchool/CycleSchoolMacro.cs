using AccessibleTrader.StrategyLab.Macro;

namespace AccessibleTrader.StrategyLab.CycleSchool;

/// <summary>One macro test's result (prereg section 6).</summary>
public sealed class MacroResult
{
    public string Id = "";
    public string Statistic = "";
    public int DevA, DevB, HeldA, HeldB;      // events per group (A = the thesis group)
    public double DevDiff = double.NaN, HeldDiff = double.NaN, P = double.NaN;
    public Dictionary<string, object> Descriptive = new();
    public bool Testable => DevA >= 20 && DevB >= 20;
    public bool HeldCheckable => HeldA >= 10 && HeldB >= 10;
}

/// <summary>
/// Camel's macro theses as fixed event tests (prereg C1 to C6, C4 descriptive). Every revised
/// series is read as published that day through <see cref="MacroPointInTime.AsOf"/>; first prints
/// come from <see cref="MacroPointInTime.FirstReleases"/> (only genuine initial prints); NBER dates
/// from the announcement table, never USREC. Outcomes start at the close of the first bar whose
/// close is after the 8:30 am Eastern release: the event day's bar (BTC's UTC day closes at 8 pm
/// Eastern), or the next trading day when markets were shut.
/// </summary>
public static class CycleSchoolMacro
{
    public sealed record ReleaseDay(DateOnly Day, DateOnly Period, double FirstValue);

    /// <summary>Release days of a vintaged series: the first-print date of each genuine initial print;
    /// on a day that printed two new months (October and November 2025 payrolls), the newest one.</summary>
    public static List<ReleaseDay> ReleaseDays(List<VintageRow> rows) =>
        MacroPointInTime.FirstReleases(rows).Where(f => f.FirstIsInitialPrint)
            .GroupBy(f => f.FirstReleaseDate).Select(g => g.OrderBy(f => f.Period).Last())
            .Select(f => new ReleaseDay(f.FirstReleaseDate, f.Period, f.FirstValue)).OrderBy(r => r.Day).ToList();

    private static int FirstBarOnOrAfter(Bar[] b, DateOnly d)
    {
        int lo = 0, hi = b.Length;
        while (lo < hi) { int m = (lo + hi) >> 1; if (b[m].Day < d) lo = m + 1; else hi = m; }
        return lo;
    }

    /// <summary>The lowest low of bars idx+1 to idx+h ÷ close(idx), − 1. NaN past the data.</summary>
    public static double MaxDrawdown(Bar[] b, int idx, int h)
    {
        if (idx >= b.Length || idx + h >= b.Length) return double.NaN;
        double lo = double.MaxValue;
        for (int k = idx + 1; k <= idx + h; k++) lo = Math.Min(lo, b[k].Low);
        return lo / b[idx].Close - 1;
    }

    private static Dictionary<DateOnly, int> Index(List<KnownValue> s)
    {
        var d = new Dictionary<DateOnly, int>();
        for (int i = 0; i < s.Count; i++) d[s[i].Date] = i;
        return d;
    }

    private static double Mean(IEnumerable<double> x)
    {
        var l = x.Where(v => !double.IsNaN(v)).ToList();
        return l.Count == 0 ? double.NaN : l.Average();
    }

    /// <summary>Permutation of group labels within calendar decades; one-sided p for "A − B &lt; 0".</summary>
    private static double DecadePermutation(List<(DateOnly Day, bool A, double Y)> ev, double observed, string test, int draws)
    {
        var byDecade = ev.GroupBy(e => e.Day.Year / 10).Select(g => g.ToList()).ToList();
        int le = 0;
        for (int k = 0; k < draws; k++)
        {
            var rng = CycleSchoolCore.Rng(test, "decade", k);
            double sa = 0, sb = 0; int na = 0, nb = 0;
            foreach (var g in byDecade)
            {
                var labels = g.Select(e => e.A).ToArray();
                for (int i = labels.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (labels[i], labels[j]) = (labels[j], labels[i]); }
                for (int i = 0; i < g.Count; i++) { if (labels[i]) { sa += g[i].Y; na++; } else { sb += g[i].Y; nb++; } }
            }
            double d = na == 0 || nb == 0 ? double.NaN : sa / na - sb / nb;
            if (!double.IsNaN(d) && d <= observed) le++;
        }
        return (1.0 + le) / (1.0 + draws);
    }

    private static void Fill(MacroResult r, List<(DateOnly Day, bool A, double Y)> all, string test, int draws, bool permute = true)
    {
        var dev = all.Where(e => e.Day < AssetRun.EraCut && !double.IsNaN(e.Y)).ToList();
        var held = all.Where(e => e.Day >= AssetRun.EraCut && !double.IsNaN(e.Y)).ToList();
        r.DevA = dev.Count(e => e.A); r.DevB = dev.Count(e => !e.A);
        r.HeldA = held.Count(e => e.A); r.HeldB = held.Count(e => !e.A);
        r.DevDiff = Mean(dev.Where(e => e.A).Select(e => e.Y)) - Mean(dev.Where(e => !e.A).Select(e => e.Y));
        r.HeldDiff = Mean(held.Where(e => e.A).Select(e => e.Y)) - Mean(held.Where(e => !e.A).Select(e => e.Y));
        if (permute && r.Testable) r.P = DecadePermutation(dev, r.DevDiff, test, draws);
    }

    // ── C1 ───────────────────────────────────────────────────────────────────

    public static MacroResult C1(CycleSchoolInputs io, Bar[] asset, string id, DateOnly from, int draws, bool countsOnly)
    {
        var rows = io.Vintages("macro/alfred/PAYEMS_vintages.csv");
        var dgs2 = io.Known("macro/fred/DGS2.csv");
        var d2 = Index(dgs2);
        var days = ReleaseDays(rows);
        var hs = new List<double>();
        var ev = new List<(DateOnly, bool, double)>();
        var fwd2 = new List<(bool A, double V)>();
        foreach (var rd in days)
        {
            var asOf = MacroPointInTime.AsOf(rows.Where(x => x.Period >= rd.Period.AddMonths(-1) && x.Period <= rd.Period), rd.Day);
            if (!asOf.TryGetValue(rd.Period.AddMonths(-1), out var prev)) { hs.Add(double.NaN); continue; }
            double h = rd.FirstValue - prev;
            var past = hs.Where(v => !double.IsNaN(v)).TakeLast(12).ToList();
            hs.Add(h);
            if (past.Count < 12 || rd.Day < from) continue;
            if (!(h > CycleSchoolCore.Median(past))) continue;
            if (!d2.TryGetValue(rd.Day, out var k) || k == 0) continue;
            double r = dgs2[k].Value - dgs2[k - 1].Value;
            if (r == 0) continue;
            int idx = FirstBarOnOrAfter(asset, rd.Day);
            double y = countsOnly ? 0 : MaxDrawdown(asset, idx, 60);
            if (!countsOnly && double.IsNaN(y)) continue;
            ev.Add((rd.Day, r < 0, y));
            if (!countsOnly && k + 60 < dgs2.Count) fwd2.Add((r < 0, dgs2[k + 60].Value - dgs2[k].Value));
        }
        var res = new MacroResult { Id = id, Statistic = "mean 60-bar max drawdown, bonds disagree (strong print, 2-year yield down) minus agree (yield up); thesis: < 0" };
        Fill(res, ev, id, draws, permute: !countsOnly);
        if (!countsOnly)
        {
            res.Descriptive["dgs2_60obs_change_disagree"] = Mean(fwd2.Where(f => f.A).Select(f => f.V));
            res.Descriptive["dgs2_60obs_change_agree"] = Mean(fwd2.Where(f => !f.A).Select(f => f.V));
        }
        return res;
    }

    // ── C2 ───────────────────────────────────────────────────────────────────

    public static MacroResult C2(CycleSchoolInputs io, Bar[] tlt, int draws, bool countsOnly)
    {
        var rows = io.Vintages("macro/alfred/CPIAUCSL_vintages.csv");
        var dgs10 = io.Known("macro/fred/DGS10.csv");
        var d10 = Index(dgs10);
        var pis = new List<double>();
        var ev = new List<(DateOnly, bool, double)>();
        var tltRet = new List<(bool A, double V)>();
        foreach (var rd in ReleaseDays(rows))
        {
            var asOf = MacroPointInTime.AsOf(rows.Where(x => x.Period >= rd.Period.AddMonths(-1) && x.Period <= rd.Period), rd.Day);
            if (!asOf.TryGetValue(rd.Period.AddMonths(-1), out var prev)) continue; // e.g. November 2025 (October 2025 does not exist)
            double pi = rd.FirstValue / prev - 1;
            var past = pis.TakeLast(12).ToList();
            pis.Add(pi);
            if (past.Count < 12 || !(pi > CycleSchoolCore.Median(past))) continue;
            if (!d10.TryGetValue(rd.Day, out var k) || k == 0) continue;
            double r = dgs10[k].Value - dgs10[k - 1].Value;
            if (r == 0) continue;
            if (k + 60 >= dgs10.Count && !countsOnly) continue;
            double y = countsOnly ? 0 : dgs10[k + 60].Value - dgs10[k].Value;
            ev.Add((rd.Day, r < 0, y));
            if (!countsOnly)
            {
                int idx = FirstBarOnOrAfter(tlt, rd.Day);
                if (idx + 60 < tlt.Length) tltRet.Add((r < 0, tlt[idx + 60].Close / tlt[idx].Close - 1));
            }
        }
        var res = new MacroResult { Id = "C2", Statistic = "mean change in DGS10 over the next 60 observations, hot CPI with the 10-year down minus hot CPI with it up; thesis: < 0" };
        Fill(res, ev, "C2", draws, permute: !countsOnly);
        if (!countsOnly)
        {
            res.Descriptive["tlt_60bar_return_disagree"] = Mean(tltRet.Where(f => f.A).Select(f => f.V));
            res.Descriptive["tlt_60bar_return_agree"] = Mean(tltRet.Where(f => !f.A).Select(f => f.V));
        }
        return res;
    }

    // ── C3 ───────────────────────────────────────────────────────────────────

    public static MacroResult C3(CycleSchoolInputs io, Bar[] spy, List<(DateOnly Date, string Type, DateOnly Month, DateOnly? Announced)> nber, int draws, bool countsOnly)
    {
        var rows = io.Vintages("macro/alfred/PAYEMS_vintages.csv");
        var days = ReleaseDays(rows);
        var rho = new List<double>();
        foreach (var rd in days)
        {
            var sub = rows.Where(x => x.Period >= rd.Period.AddMonths(-2) && x.Period < rd.Period).ToList();
            var now = MacroPointInTime.AsOf(sub, rd.Day);
            var before = MacroPointInTime.AsOf(sub, rd.Day.AddDays(-1));
            double s = 0; bool ok = true;
            foreach (var p in new[] { rd.Period.AddMonths(-1), rd.Period.AddMonths(-2) })
            {
                if (!now.TryGetValue(p, out var a) || !before.TryGetValue(p, out var b)) { ok = false; break; }
                s += a - b;
            }
            rho.Add(ok ? s : double.NaN);
        }
        var sums = new List<double>();
        for (int i = 0; i < days.Count; i++)
            sums.Add(i >= 2 ? rho[i] + rho[i - 1] + rho[i - 2] : double.NaN);
        var seq = new List<(DateOnly Day, bool Signal, double Y)>();
        var nberPeaks = nber.Where(t => t.Type == "peak").Select(t => t.Month).ToList();
        int peakSig = 0, peakOther = 0;
        for (int i = 0; i < days.Count; i++)
        {
            if (double.IsNaN(sums[i])) continue;
            var earlier = sums.Take(i).Where(v => !double.IsNaN(v)).ToList();
            if (earlier.Count < 60 || days[i].Day < spy[0].Day) continue;
            bool signal = sums[i] <= CycleSchoolCore.Quantile(earlier, 0.20);
            int idx = FirstBarOnOrAfter(spy, days[i].Day);
            double y = countsOnly ? 0 : MaxDrawdown(spy, idx, 120);
            if (!countsOnly && double.IsNaN(y)) continue;
            seq.Add((days[i].Day, signal, y));
            if (!countsOnly && nberPeaks.Any(m => m > days[i].Day && m <= days[i].Day.AddMonths(12))) { if (signal) peakSig++; else peakOther++; }
        }
        var res = new MacroResult { Id = "C3", Statistic = "mean 120-bar SPY max drawdown after a downward-revision signal minus other release days; thesis: < 0" };
        Fill(res, seq, "C3", draws, permute: false);
        res.Descriptive["nber_peak_within_12m_signal_days"] = peakSig;
        res.Descriptive["nber_peak_within_12m_other_days"] = peakOther;
        if (!countsOnly && res.Testable)
        {
            // Circular shift of the signal labels along the development sequence, offset ≥ 6 releases.
            var dev = seq.Where(e => e.Day < AssetRun.EraCut).ToList();
            int n = dev.Count, le = 0;
            for (int k = 0; k < draws; k++)
            {
                var rng = CycleSchoolCore.Rng("C3", "SPY", k);
                int off = rng.Next(6, n - 6 + 1);
                double sa = 0, sb = 0; int na = 0, nb = 0;
                for (int i = 0; i < n; i++)
                {
                    bool lab = dev[(i + off) % n].Signal;
                    if (lab) { sa += dev[i].Y; na++; } else { sb += dev[i].Y; nb++; }
                }
                double d = sa / na - sb / nb;
                if (d <= res.DevDiff) le++;
            }
            res.P = (1.0 + le) / (1.0 + draws);
        }
        return res;
    }

    // ── C4 (descriptive) ─────────────────────────────────────────────────────

    public static List<Dictionary<string, object>> C4(Dictionary<string, Bar[]> assets, List<(DateOnly Date, string Type, DateOnly Month, DateOnly? Announced)> nber)
    {
        var res = new List<Dictionary<string, object>>();
        foreach (var t in nber.Where(t => t.Announced is not null).OrderBy(t => t.Announced))
            foreach (var (name, b) in assets)
            {
                var ann = t.Announced!.Value;
                if (ann < b[0].Day.AddDays(365) || ann > b[^1].Day) continue;
                int idx = FirstBarOnOrAfter(b, ann);
                if (idx >= b.Length) continue;
                var before = b.Where(x => x.Day >= ann.AddDays(-365) && x.Day < ann).Select(x => x.Close).DefaultIfEmpty(double.NaN).Min();
                var afterBars = b.Where(x => x.Day > ann && x.Day <= ann.AddDays(365)).ToList();
                double after = afterBars.Count == 0 ? double.NaN : afterBars.Min(x => x.Close);
                res.Add(new()
                {
                    ["asset"] = name, ["type"] = t.Type, ["turning_point"] = t.Month.ToString("yyyy-MM"),
                    ["announced"] = ann.ToString("yyyy-MM-dd"), ["lag_days"] = ann.DayNumber - t.Month.DayNumber,
                    ["close_vs_low_365d_before"] = b[idx].Close / before - 1,
                    ["close_vs_low_365d_after"] = afterBars.Count < 200 ? double.NaN : b[idx].Close / after - 1,
                });
            }
        return res;
    }

    // ── C5 ───────────────────────────────────────────────────────────────────

    public static MacroResult C5(CycleSchoolInputs io, bool countsOnly, string yieldFile = "macro/fred/DGS3MO.csv", string id = "C5")
    {
        var target = CycleSchoolScore.PolicyTarget(io);
        var y = io.Known(yieldFile);
        var meetings = io.Fomc();
        double T(DateOnly d) => CycleSchoolInputs.OnOrBefore(target, d)?.Value ?? double.NaN;
        int Decision(DateOnly d)
        {
            double a = T(d.AddDays(1)) - T(d.AddDays(-1));
            return double.IsNaN(a) ? int.MinValue : Math.Abs(a) < 1e-9 ? 0 : Math.Sign(a);
        }
        var sched = meetings.Where(m => m.Scheduled).Select(m => m.Date).ToList();
        int devB = 0, devC = 0, devN = 0, devBondRight = 0, devPrevRight = 0;
        int heldN = 0, heldBondRight = 0, heldPrevRight = 0;
        for (int i = 1; i < sched.Count; i++)
        {
            var d = sched[i];
            if (d.Year < 2000) continue;
            int dec = Decision(d), prev = Decision(sched[i - 1]);
            if (dec == int.MinValue || prev == int.MinValue) continue;
            var yb = CycleSchoolInputs.OnOrBefore(y, d.AddDays(-1));
            if (yb is null) continue;
            double s = yb.Value - T(d.AddDays(-1));
            if (double.IsNaN(s)) continue;
            int pred = s <= -0.25 ? -1 : s >= 0.25 ? 1 : 0;
            bool bondRight = pred == dec, prevRight = prev == dec;
            if (d < AssetRun.EraCut)
            {
                devN++;
                if (bondRight) devBondRight++;
                if (prevRight) devPrevRight++;
                if (bondRight && !prevRight) devB++;
                if (!bondRight && prevRight) devC++;
            }
            else
            {
                heldN++;
                if (bondRight) heldBondRight++;
                if (prevRight) heldPrevRight++;
            }
        }
        var res = new MacroResult
        {
            Id = id, Statistic = $"accuracy of {Path.GetFileNameWithoutExtension(yieldFile)} pricing minus accuracy of 'same as the last scheduled meeting'; thesis: > 0 (exact McNemar)",
            DevA = devN, DevB = devN, HeldA = heldN, HeldB = heldN,
        };
        res.Descriptive["unscheduled_meetings"] = meetings.Count(m => !m.Scheduled);
        if (!countsOnly)
        {
            res.DevDiff = devN == 0 ? double.NaN : (double)(devBondRight - devPrevRight) / devN;
            res.HeldDiff = heldN == 0 ? double.NaN : (double)(heldBondRight - heldPrevRight) / heldN;
            res.P = CycleSchoolStats.BinomUpper(devB, devB + devC);
            res.Descriptive["dev_bond_right_prev_wrong"] = devB;
            res.Descriptive["dev_bond_wrong_prev_right"] = devC;
            res.Descriptive["dev_accuracy_bond"] = devN == 0 ? double.NaN : (double)devBondRight / devN;
            res.Descriptive["dev_accuracy_previous"] = devN == 0 ? double.NaN : (double)devPrevRight / devN;
        }
        return res;
    }

    // ── C6 ───────────────────────────────────────────────────────────────────

    public static MacroResult C6(CycleSchoolInputs io, AssetRun spy, int draws, bool countsOnly)
    {
        var b = spy.B;
        int n = b.Length;
        var sma50 = CycleSchoolCore.Sma(b.Select(x => x.Close).ToList(), 50);
        var sma200 = CycleSchoolCore.Sma(b.Select(x => x.Close).ToList(), 200);
        var baa = io.Known("macro/fred/BAA10Y.csv");
        var curve = io.Known("macro/fred/T10Y3M.csv");
        var baaChg = CycleSchoolInputs.ChangeOver(baa, 91, 1);
        var baaPct = CycleSchoolCore.ExpandingPct(baaChg.Select(o => o.Value).ToList(), 252);
        var curveChg = CycleSchoolInputs.ChangeOver(curve, 182, 1);
        bool[] alarm = new bool[n], i1 = new bool[n], i2 = new bool[n];
        int pb = -1, pc = -1;
        for (int d = 0; d < n; d++)
        {
            var day = b[d].Day;
            while (pb + 1 < baaChg.Count && baaChg[pb + 1].Date <= day) pb++;
            while (pc + 1 < curveChg.Count && curveChg[pc + 1].Date <= day) pc++;
            bool a1 = pb >= 0 && day.DayNumber - baaChg[pb].Date.DayNumber <= 7 && !double.IsNaN(baaPct[pb]) && baaPct[pb] >= 90;
            bool a2 = false;
            if (pc >= 0 && day.DayNumber - curveChg[pc].Date.DayNumber <= 7)
            {
                var window = curveChg.Where(o => o.Date >= day.AddDays(-365) && o.Date < day).Select(o => o.Value).ToList();
                if (window.Count > 2)
                {
                    double m = window.Average(), sd = Math.Sqrt(window.Sum(v => (v - m) * (v - m)) / (window.Count - 1));
                    a2 = curveChg[pc].Value >= 2 * sd;
                }
            }
            alarm[d] = a1 || a2;
            i2[d] = !double.IsNaN(sma50[d]) && b[d].Close < sma50[d];
            // (i) the most recent reference low known on d has been closed below, first within the last 10 bars.
            RefLow? last = null;
            foreach (var r in spy.GD) if (r.Confirm <= d && (last is null || r.Low > last.Value.Low)) last = r;
            if (last is { } j && b[d].Close < b[j.Low].Low)
            {
                int first = -1;
                for (int k = j.Low + 1; k <= d; k++) if (b[k].Close < b[j.Low].Low) { first = k; break; }
                i1[d] = first >= d - 9;
            }
        }
        List<int> Firsts(Func<int, bool> cond)
        {
            var l = new List<int>();
            int lastFire = int.MinValue / 2;
            for (int d = 0; d < n; d++) if (cond(d) && d - lastFire > 20) { l.Add(d); lastFire = d; }
            return l;
        }
        // "First firing in any 20 bars": a day counts unless a counted day of the same kind is in the
        // 20 bars before it (the same refractory reading as ALERT's "no ALERT in the previous 20 bars").
        var gate = Firsts(d => i1[d] && i2[d] && alarm[d]);
        var comp = Firsts(d => i1[d] && i2[d] && !alarm[d]);
        var ev = gate.Select(d => (b[d].Day, true, countsOnly ? 0 : MaxDrawdown(b, d, 60)))
            .Concat(comp.Select(d => (b[d].Day, false, countsOnly ? 0 : MaxDrawdown(b, d, 60)))).ToList();
        var res = new MacroResult { Id = "C6", Statistic = "mean 60-bar SPY max drawdown on gate days (failed cycle + below SMA 50 + alarm) minus the same without the alarm; thesis: < 0. BAA is investment grade, not high yield" };
        Fill(res, ev, "C6", draws, permute: false);
        if (!countsOnly && res.Testable)
        {
            var both = Enumerable.Range(0, n).Where(d => i1[d] && i2[d] && b[d].Day < AssetRun.EraCut).ToList();
            var byDecade = both.GroupBy(d => b[d].Day.Year / 10).Select(g => g.ToList()).ToList();
            int le = 0;
            for (int k = 0; k < draws; k++)
            {
                var rng = CycleSchoolCore.Rng("C6", "SPY", k);
                var lab = new bool[n];
                foreach (var g in byDecade)
                {
                    var labels = g.Select(d => alarm[d]).ToArray();
                    for (int i = labels.Length - 1; i > 0; i--) { int q = rng.Next(i + 1); (labels[i], labels[q]) = (labels[q], labels[i]); }
                    for (int i = 0; i < g.Count; i++) lab[g[i]] = labels[i];
                }
                var g2 = Firsts(d => i1[d] && i2[d] && lab[d] && b[d].Day < AssetRun.EraCut);
                var c2 = Firsts(d => i1[d] && i2[d] && !lab[d] && b[d].Day < AssetRun.EraCut);
                double dg = Mean(g2.Select(d => MaxDrawdown(b, d, 60))), dc = Mean(c2.Select(d => MaxDrawdown(b, d, 60)));
                if (!double.IsNaN(dg - dc) && dg - dc <= res.DevDiff) le++;
            }
            res.P = (1.0 + le) / (1.0 + draws);
        }
        if (!countsOnly)
        {
            var below200 = Firsts(d => d > 0 && !double.IsNaN(sma200[d]) && !double.IsNaN(sma200[d - 1]) && b[d].Close < sma200[d] && b[d - 1].Close >= sma200[d - 1]);
            res.Descriptive["reference_first_close_below_sma200_dev_mean_dd60"] = Mean(below200.Where(d => b[d].Day < AssetRun.EraCut).Select(d => MaxDrawdown(b, d, 60)));
            res.Descriptive["reference_first_close_below_sma200_dev_n"] = below200.Count(d => b[d].Day < AssetRun.EraCut);
            res.Descriptive["gate_dev_mean_dd60"] = Mean(gate.Where(d => b[d].Day < AssetRun.EraCut).Select(d => MaxDrawdown(b, d, 60)));
            res.Descriptive["comparison_dev_mean_dd60"] = Mean(comp.Where(d => b[d].Day < AssetRun.EraCut).Select(d => MaxDrawdown(b, d, 60)));
        }
        return res;
    }
}
