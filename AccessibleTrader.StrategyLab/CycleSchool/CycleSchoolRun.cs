namespace AccessibleTrader.StrategyLab.CycleSchool;

/// <summary>Development era: call bars before 2023-01-01; held-out: from it (prereg 3.1).</summary>
public enum Era { Dev, Held }

/// <summary>
/// Everything the study computes on one asset, from its bars alone plus the external metrics:
/// reference lows of both degrees, the checklist's calls (daily and weekly), the capitulation
/// score and its events, the N2-eligible bars, and a hit lookup per scoring window. Built once on
/// the clean data and once per noise seed.
/// </summary>
public sealed class AssetRun
{
    public static readonly DateOnly EraCut = new(2023, 1, 1);

    public required Asset A;
    public int N => A.Bars.Length;
    public Bar[] B => A.Bars;
    public int BarsPerWeek => A.Crypto ? 7 : 5;
    public required double[] Atr, Sma10;
    public required List<RefLow> GD, GW;
    public required bool[] UnresGD, UnresGW;

    // Camel's checklist, daily degree.
    public required List<CycleSchoolCore.ChecklistBar> AP, AV1, AV2, AF2, ANW;
    public required Dictionary<int, List<int>> SwingElig;
    // Weekly degree (A7): calls and N2-eligible bars mapped to daily bars, keyed by the weekly swing low.
    public required List<int> A7;
    public required Dictionary<int, int> A7Anchor;          // daily call bar → daily low bar of its swing week
    public required Dictionary<int, List<int>> A7Elig;

    // The capitulation score and its events. Null when the score is not built (counts of the checklist only).
    public CapScore? Cap;
    public List<int> Alert = new(), AlertP = new(), AlertND = new(), AlertW = new(), Alert90 = new(), AlertP90 = new();
    public List<CycleSchoolCore.Confirmation> Conf = new(), ConfP = new();
    public Dictionary<int, List<int>> ConfElig = new();

    public Era EraOf(int t) => B[t].Day < EraCut ? Era.Dev : Era.Held;

    // ── Hit lookups ──────────────────────────────────────────────────────────

    private readonly Dictionary<string, CycleSchoolCore.Scored?[]> _hits = new();

    /// <summary>Window ids: "D-post" (low 0 to 15 bars before), "D-alert" (5 before to 20 after),
    /// "W-post" (0 to 15 weeks before), "W-alert" (5 weeks before to 20 after).</summary>
    public CycleSchoolCore.Scored?[] Hits(string window)
    {
        if (_hits.TryGetValue(window, out var h)) return h;
        var (w, lows, unres) = window switch
        {
            "D-post" => (new CycleSchoolCore.HitWindow(15, 0), GD, UnresGD),
            "D-alert" => (new CycleSchoolCore.HitWindow(5, 20), GD, UnresGD),
            "W-post" => (new CycleSchoolCore.HitWindow(15 * BarsPerWeek, 0), GW, UnresGW),
            "W-alert" => (new CycleSchoolCore.HitWindow(5 * BarsPerWeek, 20 * BarsPerWeek), GW, UnresGW),
            _ => throw new ArgumentException(window),
        };
        h = new CycleSchoolCore.Scored?[N];
        for (int t = 0; t < N; t++) h[t] = CycleSchoolCore.Score(t, w, lows, unres, N);
        _hits[window] = h;
        return h;
    }

    /// <summary>The era's scorable bars [a, z] for a window (where a call could be scored).</summary>
    public (int A, int Z) Scorable(string window, Era era)
    {
        var h = Hits(window);
        int a = -1, z = -1;
        for (int t = 0; t < N; t++)
        {
            if (h[t] is null || EraOf(t) != era) continue;
            if (a < 0) a = t;
            z = t;
        }
        return (a, z);
    }

    public static AssetRun Build(Asset a, IReadOnlyList<Asset>? universe, CycleSchoolScore.External? x,
        Func<string, DateOnly, double>? priceFactor = null, bool withScore = true)
    {
        var b = a.Bars;
        var atr = CycleSchoolCore.Atr(b);
        var sma10 = CycleSchoolCore.Sma(b.Select(z => z.Close).ToList(), 10);
        var gd = CycleSchoolCore.ReferenceLows(b, atr, 20, 3, out var ugd);
        var gw = CycleSchoolCore.ReferenceLows(b, atr, 130, 6, out var ugw);
        double L = a.Name is "BTC" or "ETH" ? 60 : a.Name == "GLD" ? 25 : 40;
        var ci = CycleSchoolCore.DailyChecklistInputs(b, gd, L);
        int from = FirstDefined(ci);

        // Weekly checklist on completed weekly bars, scored against G_W (prereg 4.3).
        var weeks = CycleSchoolCore.Weekly(b, a.Crypto);
        var wb = weeks.Select(w => w.Bar).ToList();
        int WeekOf(int daily)
        {
            int lo = 0, hi = weeks.Count - 1;
            while (lo < hi) { int m = (lo + hi) >> 1; if (weeks[m].LastDaily < daily) lo = m + 1; else hi = m; }
            return lo;
        }
        double wl = a.Name is "BTC" or "ETH" ? 30 : a.Name == "GLD" ? 26 : 24;
        var (wdss, wtrig) = CycleSchoolCore.Dss(wb);
        var wci = new CycleSchoolCore.ChecklistInputs
        {
            Bars = wb, Atr = CycleSchoolCore.Atr(wb), Sma10 = CycleSchoolCore.Sma(wb.Select(z => z.Close).ToList(), 10),
            Dss = wdss, Trigger = wtrig, Lows = gw, WindowLength = wl,
            // A G_W low counts for week i once it is confirmed by that week's end.
            LowOnThisFrame = r => (WeekOf(r.Low), WeekOf(r.Confirm)),
        };
        int wfrom = FirstDefined(wci);
        var a7 = new List<int>();
        var a7Anchor = new Dictionary<int, int>();
        foreach (var c in CycleSchoolCore.ChecklistCalls(wci, CycleSchoolCore.RulePrimary, wfrom))
        {
            int d = weeks[c.I].UsableFrom;
            if (d == int.MaxValue) continue;
            a7.Add(d);
            a7Anchor[d] = LowestDaily(b, weeks[c.J]);
        }
        var a7Elig = new Dictionary<int, List<int>>();
        foreach (var (j, bars) in CycleSchoolCore.SwingEligible(wb, wfrom))
        {
            var mapped = bars.Select(i => weeks[i].UsableFrom).Where(d => d != int.MaxValue).ToList();
            if (mapped.Count > 0) a7Elig[j] = mapped;
        }

        var run = new AssetRun
        {
            A = a, Atr = atr, Sma10 = sma10, GD = gd, GW = gw, UnresGD = ugd, UnresGW = ugw,
            AP = CycleSchoolCore.ChecklistCalls(ci, CycleSchoolCore.RulePrimary, from),
            AV1 = CycleSchoolCore.ChecklistCalls(ci, CycleSchoolCore.RuleAnyOne, from),
            AV2 = CycleSchoolCore.ChecklistCalls(ci, CycleSchoolCore.RulePivotDssTl, from),
            AF2 = CycleSchoolCore.ChecklistCalls(ci, CycleSchoolCore.RuleTwoOfFour, from),
            ANW = CycleSchoolCore.ChecklistCalls(ci, CycleSchoolCore.RuleNoWindow, from),
            SwingElig = CycleSchoolCore.SwingEligible(b, from),
            A7 = a7, A7Anchor = a7Anchor, A7Elig = a7Elig,
        };
        if (!withScore || x == null) return run;

        var cap = CycleSchoolScore.Build(a, universe, x, priceFactor);
        run.Cap = cap;
        run.Alert = CycleSchoolCore.Alerts(cap.PctCap, gd, 95, useDue: true);
        run.AlertP = CycleSchoolCore.Alerts(cap.PctCapP, gd, 95, useDue: true);
        run.AlertND = CycleSchoolCore.Alerts(cap.PctCap, gd, 95, useDue: false);
        run.AlertW = CycleSchoolCore.Alerts(cap.PctCap, gw, 95, useDue: true);
        run.Alert90 = CycleSchoolCore.Alerts(cap.PctCap, gd, 90, useDue: true);
        run.AlertP90 = CycleSchoolCore.Alerts(cap.PctCapP, gd, 90, useDue: true);
        run.Conf = CycleSchoolCore.Confirmations(b, cap.PctCap);
        run.ConfP = CycleSchoolCore.Confirmations(b, cap.PctCapP);
        run.ConfElig = CycleSchoolCore.Confirmations(b, null, allEligible: true)
            .GroupBy(c => c.J).ToDictionary(g => g.Key, g => g.Select(c => c.I).ToList());
        return run;
    }

    private static int LowestDaily(Bar[] b, CycleSchoolCore.Week w)
    {
        int best = w.FirstDaily;
        for (int k = w.FirstDaily; k <= w.LastDaily; k++) if (b[k].Low < b[best].Low) best = k;
        return best;
    }

    /// <summary>The first bar on which the checklist's inputs (ATR, SMA 10, DSS and its trigger) are all defined.</summary>
    public static int FirstDefined(CycleSchoolCore.ChecklistInputs x)
    {
        for (int i = 1; i < x.Bars.Count; i++)
            if (!double.IsNaN(x.Atr[i - 1]) && !double.IsNaN(x.Sma10[i]) && !double.IsNaN(x.Dss[i - 1]) && !double.IsNaN(x.Trigger[i - 1]))
                return i;
        return x.Bars.Count;
    }

    // ── Noise (prereg section 8) ─────────────────────────────────────────────

    /// <summary>
    /// Varma's noise: gaussian noise on every close-to-close log return at <paramref name="alpha"/>
    /// of the series' own daily volatility, the path rebuilt. Each bar's open, high and low are moved
    /// by the same factor as its close, so candle shapes (the thrust and engulfing items) survive;
    /// <c>OnChainRobustness.Perturb</c> sets open = close and would void them. Returns the bars and
    /// the cumulative factor per bar (for MVRV and Puell).
    /// </summary>
    public static (Bar[] Bars, double[] Factor) Noise(Bar[] bars, double alpha, Random rng)
    {
        int n = bars.Length;
        var r = new double[n];
        double s = 0, ss = 0; int c = 0;
        for (int i = 1; i < n; i++) { r[i] = Math.Log(bars[i].Close / bars[i - 1].Close); s += r[i]; ss += r[i] * r[i]; c++; }
        double mean = c > 0 ? s / c : 0, sigma = c > 1 ? Math.Sqrt(Math.Max(1e-12, ss / c - mean * mean)) : 0;
        var outp = new Bar[n];
        var f = new double[n];
        double drift = 0;
        outp[0] = bars[0];
        f[0] = 1;
        for (int i = 1; i < n; i++)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            drift += alpha * sigma * g;
            double k = Math.Exp(drift);
            var x = bars[i];
            outp[i] = new Bar(x.Day, x.Open * k, x.High * k, x.Low * k, x.Close * k);
            f[i] = k;
        }
        return (outp, f);
    }

    /// <summary>
    /// N3's shuffled-bar surrogate (prereg 3.5): the development-era bars' tuples (close-to-close
    /// log return, open ÷ previous close, high ÷ close, low ÷ close) are shuffled as whole tuples and
    /// the path rebuilt from the first close. Dates stay in place.
    /// </summary>
    public static Bar[] Surrogate(Bar[] dev, Random rng)
    {
        int n = dev.Length;
        var tup = new (double R, double O, double H, double L)[n - 1];
        for (int i = 1; i < n; i++)
            tup[i - 1] = (Math.Log(dev[i].Close / dev[i - 1].Close), dev[i].Open / dev[i - 1].Close, dev[i].High / dev[i].Close, dev[i].Low / dev[i].Close);
        for (int i = tup.Length - 1; i > 0; i--) { int k = rng.Next(i + 1); (tup[i], tup[k]) = (tup[k], tup[i]); }
        var o = new Bar[n];
        o[0] = dev[0];
        double prev = dev[0].Close;
        for (int i = 1; i < n; i++)
        {
            var t = tup[i - 1];
            double close = prev * Math.Exp(t.R), open = prev * t.O;
            double high = Math.Max(close * t.H, Math.Max(open, close)), low = Math.Min(close * t.L, Math.Min(open, close));
            o[i] = new Bar(dev[i].Day, open, high, low, close);
            prev = close;
        }
        return o;
    }
}
