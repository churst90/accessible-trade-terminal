using AccessibleTrader.StrategyLab.Macro;

namespace AccessibleTrader.StrategyLab.CycleSchool;

/// <summary>An asset of the study: its class and its daily bars.</summary>
public sealed record Asset(string Name, string Class, string File, Bar[] Bars)
{
    public bool Crypto => Class == "crypto";
    public bool Equity => Class == "equity";
}

/// <summary>The capitulation score on one asset: every metric, the family scores, CAP, CAP_P and their own percentiles.</summary>
public sealed class CapScore
{
    public required List<Metric> Metrics;
    public required Dictionary<string, double[]> Families;
    public required double[] Cap;
    public required double[] CapP;
    public required double[] PctCap;
    public required double[] PctCapP;
    public required int[] FamiliesDefined;
}

/// <summary>
/// The capitulation score of <c>docs/CYCLE_LOW_INDICATOR_DESIGN.md</c> sections 3 to 5 (with the
/// prereg addendum's Q3 files and M10 to M12). Equal weights inside a family and across families;
/// every percentile expanding; CAP needs at least 3 defined families; pctCAP and pctCAP_P are the
/// scores' own expanding percentiles (252 values).
/// </summary>
public static class CycleSchoolScore
{
    public static readonly string[] FamilyOrder = { "P", "B", "V", "Q", "S", "C", "M" };

    /// <summary>The external (non-price) metrics, built once: observation lists by metric id.</summary>
    public sealed class External
    {
        public readonly Dictionary<string, List<Obs>> Obs = new();
        public readonly Dictionary<string, (int Min, int Stale, string Family)> Spec = new();
        /// <summary>Metrics whose value carries price (MVRV, Puell): scaled by the price noise factor.</summary>
        public readonly Dictionary<string, string> PriceScaled = new();
        public List<(DateOnly Day, long Ts, double V)> FundingBtc = new(), FundingEth = new();
    }

    public static External LoadExternal(CycleSchoolInputs io)
    {
        var x = new External();
        void Add(string id, string fam, int min, int stale, List<Obs> o) { x.Obs[id] = o; x.Spec[id] = (min, stale, fam); }

        // Positioning. Q1 funding and Q2 open interest (BTC, ETH); Q3 the release-stamped COT files.
        x.FundingBtc = io.Xs("xs_binancevision_btcusdt_funding_8h.json");
        x.FundingEth = io.Xs("xs_binancevision_ethusdt_funding_8h.json");
        foreach (var (sym, fam) in new[] { ("btc", "Q"), ("eth", "Q") })
        {
            var oi = io.Xs($"xs_binancevision_{sym}usdt_oi_1d.json").GroupBy(p => p.Day).Select(g => g.Last()).ToList();
            var kv = oi.Select(p => new KnownValue(p.Day, p.V, p.Day.AddDays(1))).ToList();
            Add($"Q2_{sym}", fam, 252, 3, CycleSchoolInputs.ChangeOver(kv, 30, -1, pctChange: true));
        }
        Add("Q3_sp500", "Q", 104, 21, CycleSchoolInputs.Level(io.Known("macro/cftc/tff_sp500_emini_lev_funds_net_pct_oi.csv"), -1));
        Add("Q3_nasdaq", "Q", 104, 21, CycleSchoolInputs.Level(io.Known("macro/cftc/tff_nasdaq100_mini_lev_funds_net_pct_oi.csv"), -1));
        Add("Q3_btc", "Q", 104, 21, CycleSchoolInputs.Level(io.Known("macro/cftc/tff_bitcoin_cme_lev_funds_net_pct_oi.csv"), -1));
        Add("Q3_gold", "Q", 104, 21, CycleSchoolInputs.Level(io.Known("macro/cftc/disagg_gold_comex_managed_money_net_pct_oi.csv"), -1));

        // Sentiment.
        Add("S1", "S", 104, 14, CycleSchoolInputs.Level(io.Known("macro/aaii/aaii_bull_minus_bear.csv"), -1));
        var pc = io.Known("macro/cboe/cboe_equity_pc_stitched.csv");
        var s2 = new List<Obs>();
        for (int k = 9; k < pc.Count; k++)
        {
            double m = 0;
            for (int a = k - 9; a <= k; a++) m += pc[a].Value;
            s2.Add(new Obs(pc[k].Date, pc[k].KnownFrom, pc[k].KnownFrom, +m / 10));
        }
        Add("S2", "S", 252, 5, s2);
        Add("S3", "S", 36, 75, CycleSchoolInputs.PerVintage(io.Vintages("macro/alfred/UMCSENT_vintages.csv"), s => s.Values.Last(), -1));
        var fng = io.Xs("xs_alternativeme_fng_index_1d.json").GroupBy(p => p.Day).Select(g => g.Last())
            .Select(p => new Obs(p.Day, p.Day.AddDays(1), p.Day.AddDays(1), -p.V)).ToList();
        Add("S4", "S", 252, 3, fng);

        // On-chain.
        var mvrv = io.Known("macro/coinmetrics/btc_capmvrvcur.csv");
        Add("C1_btc", "C", 252, 3, CycleSchoolInputs.Level(mvrv, -1));
        x.PriceScaled["C1_btc"] = "BTC";
        var iss = io.Known("macro/coinmetrics/btc_isstotusd.csv");
        Add("C2", "C", 252, 3, Ratio(iss, 365, 0, -1));
        x.PriceScaled["C2"] = "BTC";
        var hr = io.Known("macro/coinmetrics/btc_hashrate.csv");
        Add("C3", "C", 252, 3, Ratio(hr, 30, 60, -1));
        var ethMvrv = io.Xs("xs_coinmetrics_eth_capmvrvcur_1d.json").GroupBy(p => p.Day).Select(g => g.Last())
            .Select(p => new Obs(p.Day, p.Day.AddDays(1), p.Day.AddDays(1), -p.V)).ToList();
        Add("C1_eth", "C", 252, 3, ethMvrv);
        x.PriceScaled["C1_eth"] = "ETH";

        // Macro and liquidity.
        Add("M1", "M", 252, 7, CycleSchoolInputs.ChangeOver(io.Known("macro/fred/T10Y3M.csv"), 182, +1));
        Add("M2", "M", 252, 7, CycleSchoolInputs.ChangeOver(io.Known("macro/fred/BAA10Y.csv"), 91, +1));
        Add("M3", "M", 252, 10, CycleSchoolInputs.ChangeOver(ChainedDollar(io), 91, +1, logRatio: true));
        Add("M4", "M", 252, 14, NetLiquidity(io));
        Add("M5", "M", 252, 7, CycleSchoolInputs.ChangeOver(PolicyTarget(io), 182, -1));
        Add("M6", "M", 252, 7, CycleSchoolInputs.Level(io.Known("macro/fred/VIXCLS.csv"), +1));
        Add("M7", "M", 36, 75, Sahm(io));
        Add("M8", "M", 104, 14, CycleSchoolInputs.PerVintage(io.Vintages("macro/alfred/ICSA_vintages.csv"), CycleSchoolInputs.ClaimsRatio, +1, window: 500));
        Add("M9", "M", 36, 75, CycleSchoolInputs.PerVintage(io.Vintages("macro/alfred/PAYEMS_vintages.csv"), CycleSchoolInputs.MeanOfLast3Changes, -1, window: 200));
        Add("M10", "M", 36, 75, CycleSchoolInputs.PerVintage(io.Vintages("macro/alfred/TEMPHELPS_vintages.csv"), CycleSchoolInputs.MeanOfLast3Changes, -1, window: 200));
        Add("M11", "M", 36, 75, CycleSchoolInputs.PerVintage(io.Vintages("macro/alfred/JTSQUR_vintages.csv"), CycleSchoolInputs.MeanOfLast3Changes, -1, window: 200));
        Add("M12", "M", 36, 75, CycleSchoolInputs.PerVintage(io.Vintages("macro/alfred/JTSJOL_vintages.csv"), CycleSchoolInputs.MeanOfLast3Changes, -1, window: 200));
        return x;
    }

    /// <summary>mean over the last <paramref name="a"/> days ÷ mean over the last <paramref name="b"/>
    /// days (b = 0: value ÷ its mean over the last <paramref name="a"/> days). Needs a full window.</summary>
    private static List<Obs> Ratio(List<KnownValue> s, int a, int b, double sign)
    {
        var res = new List<Obs>();
        int span = Math.Max(a, b);
        if (s.Count == 0) return res;
        var first = s[0].Date;
        double MeanBack(int k, int days)
        {
            double sum = 0; int n = 0;
            for (int q = k; q >= 0 && s[q].Date > s[k].Date.AddDays(-days); q--) { sum += s[q].Value; n++; }
            return n == 0 ? double.NaN : sum / n;
        }
        for (int k = 0; k < s.Count; k++)
        {
            if (s[k].Date < first.AddDays(span - 1) || s[k].KnownFrom == DateOnly.MaxValue) continue;
            double v = b == 0 ? s[k].Value / MeanBack(k, a) : MeanBack(k, a) / MeanBack(k, b);
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            res.Add(new Obs(s[k].Date, s[k].KnownFrom, s[k].KnownFrom, sign * v));
        }
        return res;
    }

    /// <summary>M3's dollar: DTWEXM to 2005-12-30, then DTWEXBGS from 2006-01-02 rescaled to join
    /// it (the splice day counts as no change).</summary>
    private static List<KnownValue> ChainedDollar(CycleSchoolInputs io)
    {
        var m = io.Known("macro/fred/DTWEXM.csv").Where(r => r.Date <= new DateOnly(2005, 12, 30)).ToList();
        var g = io.Known("macro/fred/DTWEXBGS.csv").Where(r => r.Date >= new DateOnly(2006, 1, 2)).ToList();
        if (m.Count == 0 || g.Count == 0) return m.Concat(g).ToList();
        double scale = m[^1].Value / g[0].Value;
        return m.Concat(g.Select(r => r with { Value = r.Value * scale })).ToList();
    }

    /// <summary>M5's policy rate: DFEDTAR to 2008-12-15, then the midpoint of DFEDTARU and DFEDTARL.</summary>
    public static List<KnownValue> PolicyTarget(CycleSchoolInputs io)
    {
        var t = io.Known("macro/fred/DFEDTAR.csv").Where(r => r.Date <= new DateOnly(2008, 12, 15)).ToList();
        var u = io.Known("macro/fred/DFEDTARU.csv").ToDictionary(r => r.Date);
        foreach (var l in io.Known("macro/fred/DFEDTARL.csv"))
            if (l.Date >= new DateOnly(2008, 12, 16) && u.TryGetValue(l.Date, out var up))
                t.Add(new KnownValue(l.Date, (l.Value + up.Value) / 2, l.KnownFrom > up.KnownFrom ? l.KnownFrom : up.KnownFrom));
        return t.OrderBy(r => r.Date).ToList();
    }

    /// <summary>M4: the 91-day % change of WALCL − WTREGEN − 1000 × RRPONTSYD, on every calendar day
    /// from data known by that day. RRP: the last row if at most 7 days old, else 0. Known from the
    /// latest of the three; its age from the older of WALCL's and WTREGEN's known-from dates.</summary>
    private static List<Obs> NetLiquidity(CycleSchoolInputs io)
    {
        var w = io.Known("macro/fred/WALCL.csv");
        var t = io.Known("macro/fred/WTREGEN.csv");
        var r = io.Known("macro/fred/RRPONTSYD.csv");
        var level = new List<(DateOnly D, double V, DateOnly Known, DateOnly Age)>();
        if (w.Count == 0) return new();
        var wk = w.OrderBy(x => x.KnownFrom).ToList();
        var tk = t.OrderBy(x => x.KnownFrom).ToList();
        var rk = r.OrderBy(x => x.KnownFrom).ToList();
        int iw = 0, it = 0, ir = 0;
        KnownValue? lw = null, lt = null, lr = null;
        for (var d = w[0].Date; d <= new DateOnly(2026, 10, 18); d = d.AddDays(1))
        {
            while (iw < wk.Count && wk[iw].KnownFrom <= d) { if (lw is null || wk[iw].Date > lw.Date) lw = wk[iw]; iw++; }
            while (it < tk.Count && tk[it].KnownFrom <= d) { if (lt is null || tk[it].Date > lt.Date) lt = tk[it]; it++; }
            while (ir < rk.Count && rk[ir].KnownFrom <= d) { if (lr is null || rk[ir].Date > lr.Date) lr = rk[ir]; ir++; }
            if (lw is null || lt is null) continue;
            double rrp = lr is not null && d.DayNumber - lr.Date.DayNumber <= 7 ? lr.Value : 0;
            var known = Max(lw.KnownFrom, lt.KnownFrom);
            if (lr is not null && d.DayNumber - lr.Date.DayNumber <= 7) known = Max(known, lr.KnownFrom);
            level.Add((d, lw.Value - lt.Value - 1000 * rrp, known, lw.KnownFrom < lt.KnownFrom ? lw.KnownFrom : lt.KnownFrom));
        }
        var res = new List<Obs>();
        for (int k = 91; k < level.Count; k++)
        {
            var prev = level[k - 91];
            if (prev.V == 0) continue;
            res.Add(new Obs(level[k].D, level[k].D, level[k].Age, -(level[k].V / prev.V - 1)));
        }
        return res;

        static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    }

    /// <summary>M7: the real-time Sahm rule. From its first vintage (2019-09-06) as published each
    /// vintage date; before that, each month's value (as in that first vintage, built from real-time
    /// data) is known from UNRATE's first-print date for the same month (MACRO_DATA trap 4).</summary>
    private static List<Obs> Sahm(CycleSchoolInputs io)
    {
        var rows = io.Vintages("macro/alfred/SAHMREALTIME_vintages.csv");
        var firstVintage = rows.Min(r => r.RealtimeStart);
        var unrateFirst = new Dictionary<DateOnly, DateOnly>();
        // UNRATE's first prints, derived from its own vintages (the same derivation that wrote
        // UNRATE_first_release.csv), so the dates come through MacroPointInTime.
        foreach (var f in MacroPointInTime.FirstReleases(io.Vintages("macro/alfred/UNRATE_vintages.csv")))
            if (f.FirstIsInitialPrint) unrateFirst[f.Period] = f.FirstReleaseDate;
        var res = new List<Obs>();
        foreach (var (period, value) in MacroPointInTime.AsOf(rows, firstVintage))
            if (unrateFirst.TryGetValue(period, out var k) && k < firstVintage)
                res.Add(new Obs(k, k, k, +value));
        res.AddRange(CycleSchoolInputs.PerVintage(rows, s => s.Values.Last(), +1, window: 200));
        return res;
    }

    // ── The score on one asset ───────────────────────────────────────────────

    /// <summary>
    /// Builds every metric for <paramref name="a"/> on its bars. <paramref name="universe"/> is the
    /// breadth peer group (null for gold). <paramref name="priceFactor"/> maps a day to the noise
    /// run's cumulative price factor for MVRV and Puell (null when clean).
    /// </summary>
    public static CapScore Build(Asset a, IReadOnlyList<Asset>? universe, External x, Func<string, DateOnly, double>? priceFactor = null,
        IReadOnlySet<string>? dropFamilies = null)
    {
        var b = a.Bars;
        int n = b.Length;
        var days = b.Select(z => z.Day).ToList();
        var metrics = new List<Metric>();
        void M(string id, string fam, double[] pct) { if (dropFamilies == null || !dropFamilies.Contains(fam)) metrics.Add(new Metric(id, fam, pct)); }

        // P: price and time.
        var close = b.Select(z => z.Close).ToList();
        M("P1", "P", CycleSchoolCore.ExpandingPct(Drawdown(close, 63), 252));
        M("P2", "P", CycleSchoolCore.ExpandingPct(Drawdown(close, 252), 252));
        M("P3", "P", CycleSchoolCore.ExpandingPct(Neg(CycleSchoolCore.Rsi(close)), 252));
        var (dss, _) = CycleSchoolCore.Dss(b);
        M("P5", "P", CycleSchoolCore.ExpandingPct(Neg(dss), 252));
        var sma200 = CycleSchoolCore.Sma(close, 200);
        M("P7", "P", CycleSchoolCore.ExpandingPct(Enumerable.Range(0, n).Select(i => -(close[i] - sma200[i]) / sma200[i]).ToArray(), 252));
        var weeks = CycleSchoolCore.Weekly(b, a.Crypto);
        var wbars = weeks.Select(w => w.Bar).ToList();
        var wAt = CycleSchoolCore.WeekAt(weeks, n);
        var wRsi = CycleSchoolCore.ExpandingPct(Neg(CycleSchoolCore.Rsi(wbars.Select(w => w.Close).ToList())), 104);
        var (wDss, _) = CycleSchoolCore.Dss(wbars);
        var wDssPct = CycleSchoolCore.ExpandingPct(Neg(wDss), 104);
        M("P4", "P", Enumerable.Range(0, n).Select(i => wAt[i] < 0 ? double.NaN : wRsi[wAt[i]]).ToArray());
        M("P6", "P", Enumerable.Range(0, n).Select(i => wAt[i] < 0 ? double.NaN : wDssPct[wAt[i]]).ToArray());

        // B: breadth.
        if (universe != null)
        {
            var (b1, b2) = Breadth(days, universe);
            M("B1", "B", CycleSchoolCore.ExpandingPct(Neg(b1), 252));
            M("B2", "B", CycleSchoolCore.ExpandingPct(b2, 252));
        }

        // V: volatility.
        var lr = new double[n];
        for (int i = 1; i < n; i++) lr[i] = Math.Log(close[i] / close[i - 1]);
        var v1 = CycleSchoolCore.Nan(n); var v2 = CycleSchoolCore.Nan(n);
        for (int i = 60; i < n; i++)
        {
            v1[i] = Sd(lr, i - 19, i);
            double s10 = Sd(lr, i - 9, i), s60 = Sd(lr, i - 59, i);
            v2[i] = s60 > 0 ? s10 / s60 : double.NaN;
        }
        for (int i = 20; i < 60 && i < n; i++) v1[i] = Sd(lr, i - 19, i);
        M("V1", "V", CycleSchoolCore.ExpandingPct(v1, 252));
        M("V2", "V", CycleSchoolCore.ExpandingPct(v2, 252));

        double[] R(string id, bool strict = false)
        {
            var (min, stale, _) = x.Spec[id];
            var obs = x.Obs[id];
            if (priceFactor != null && x.PriceScaled.TryGetValue(id, out var sym))
                obs = obs.Select(o => o with { Value = o.Value * priceFactor(sym, o.Date) }).ToList();
            return CycleSchoolInputs.MapReleases(obs, min, stale, days, strict);
        }

        // Q: positioning.
        if (a.Name is "BTC" or "ETH")
        {
            var fund = a.Name == "BTC" ? x.FundingBtc : x.FundingEth;
            M("Q1", "Q", CycleSchoolInputs.MapReleases(FundingObs(fund), 252, 3, days));
            M("Q2", "Q", R(a.Name == "BTC" ? "Q2_btc" : "Q2_eth"));
        }
        string? q3 = a.Name switch { "SPY" or "DIA" or "IWM" => "Q3_sp500", "QQQ" => "Q3_nasdaq", "BTC" => "Q3_btc", "GLD" => "Q3_gold", _ => null };
        if (q3 != null) M("Q3", "Q", R(q3));

        // S: sentiment.
        if (a.Equity) { M("S1", "S", R("S1")); M("S2", "S", R("S2")); M("S3", "S", R("S3")); }
        if (a.Crypto) M("S4", "S", R("S4"));

        // C: on-chain.
        if (a.Name == "BTC") { M("C1", "C", R("C1_btc")); M("C2", "C", R("C2")); M("C3", "C", R("C3")); }
        if (a.Name == "ETH") M("C1", "C", R("C1_eth"));

        // M: macro and liquidity, every asset. M4 is the H.4.1 data: strictly before an equity bar's date.
        foreach (var id in new[] { "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8", "M9", "M10", "M11", "M12" })
            M(id, "M", R(id, strict: id == "M4" && !a.Crypto));

        // Family scores, CAP, CAP_P.
        var fams = new Dictionary<string, double[]>();
        foreach (var f in FamilyOrder)
        {
            var ms = metrics.Where(m => m.Family == f).ToList();
            if (ms.Count == 0) continue;
            var s = CycleSchoolCore.Nan(n);
            for (int i = 0; i < n; i++)
            {
                double sum = 0; int c = 0;
                foreach (var m in ms) if (!double.IsNaN(m.Pct[i])) { sum += m.Pct[i]; c++; }
                if (c > 0) s[i] = sum / c;
            }
            fams[f] = s;
        }
        var cap = CycleSchoolCore.Nan(n);
        var defined = new int[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0; int c = 0;
            foreach (var f in fams.Values) if (!double.IsNaN(f[i])) { sum += f[i]; c++; }
            defined[i] = c;
            if (c >= 3) cap[i] = sum / c;
        }
        var capP = fams.TryGetValue("P", out var fp) ? fp : CycleSchoolCore.Nan(n);
        return new CapScore
        {
            Metrics = metrics, Families = fams, Cap = cap, CapP = capP,
            PctCap = CycleSchoolCore.ExpandingPct(cap, 252), PctCapP = CycleSchoolCore.ExpandingPct(capP, 252),
            FamiliesDefined = defined,
        };
    }

    /// <summary>Q1: on UTC day D, the mean of the last 21 eight-hour prints stamped before D's close
    /// (D+1 00:00 UTC). Its age is counted from the newest print's day.</summary>
    private static List<Obs> FundingObs(List<(DateOnly Day, long Ts, double V)> f)
    {
        var res = new List<Obs>();
        if (f.Count < 21) return res;
        int k = 0;
        for (var d = f[0].Day; d <= f[^1].Day; d = d.AddDays(1))
        {
            long close = new DateTimeOffset(d.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();
            while (k < f.Count && f[k].Ts < close) k++;
            if (k < 21) continue;
            double m = 0;
            for (int q = k - 21; q < k; q++) m += f[q].V;
            res.Add(new Obs(d, d, f[k - 1].Day, -m / 21));
        }
        return res;
    }

    private static double[] Drawdown(List<double> c, int len)
    {
        var r = CycleSchoolCore.Nan(c.Count);
        for (int i = len - 1; i < c.Count; i++)
        {
            double mx = double.MinValue;
            for (int k = i - len + 1; k <= i; k++) mx = Math.Max(mx, c[k]);
            r[i] = -(c[i] / mx - 1); // more negative drawdown = more washed: oriented by −
        }
        return r;
    }

    private static double[] Neg(double[] x) => x.Select(v => -v).ToArray();

    private static double Sd(double[] x, int a, int z)
    {
        int n = z - a + 1;
        if (a < 1 || n < 2) return double.NaN;
        double m = 0;
        for (int i = a; i <= z; i++) m += x[i];
        m /= n;
        double s = 0;
        for (int i = a; i <= z; i++) s += (x[i] - m) * (x[i] - m);
        return Math.Sqrt(s / (n - 1));
    }

    /// <summary>B1 and B2 on each target day: among peers with a bar that day and at least 60 bars
    /// of history, the share closing above SMA(50) and the share with RSI(14) below 30. Defined
    /// with at least 4 such peers.</summary>
    private static (double[] Above, double[] Oversold) Breadth(List<DateOnly> days, IReadOnlyList<Asset> universe)
    {
        var above = CycleSchoolCore.Nan(days.Count);
        var over = CycleSchoolCore.Nan(days.Count);
        var peers = universe.Select(u =>
        {
            var c = u.Bars.Select(z => z.Close).ToList();
            var idx = new Dictionary<DateOnly, int>();
            for (int i = 0; i < u.Bars.Length; i++) idx[u.Bars[i].Day] = i;
            return (Idx: idx, Close: c, Sma: CycleSchoolCore.Sma(c, 50), Rsi: CycleSchoolCore.Rsi(c));
        }).ToList();
        for (int d = 0; d < days.Count; d++)
        {
            int members = 0, up = 0, os = 0;
            foreach (var p in peers)
            {
                if (!p.Idx.TryGetValue(days[d], out var i) || i < 59) continue;
                members++;
                if (p.Close[i] > p.Sma[i]) up++;
                if (p.Rsi[i] < 30) os++;
            }
            if (members < 4) continue;
            above[d] = (double)up / members;
            over[d] = (double)os / members;
        }
        return (above, over);
    }
}
