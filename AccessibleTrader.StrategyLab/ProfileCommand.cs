namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Computes asset-personality metrics directly from OHLCV snapshots — no indicator engine,
/// no strategy host. The goal is to surface what numerically distinguishes assets where
/// mechanical short setups work (XRP daily) from assets where they don't (BTC/ETH/LTC/BCH
/// daily), without baking any of these features into Pulse yet. The output is a side-by-side
/// table that becomes the empirical foundation for the AssetProfile classifier.
///
/// Metrics:
///   DriftCAGR        — annualized log-return slope (the "long-term bias")
///   AnnVol           — annualized stdev of log returns (volatility regime)
///   Sharpe           — DriftCAGR / AnnVol (signal-to-noise of the drift)
///   MaxDD            — peak-to-trough drawdown over the full window
///   PctTimeInDD      — fraction of bars where price is below the trailing 365d high
///   PctAboveSMA200   — fraction of bars where close > SMA(200) (trend persistence)
///   UpDownRatio      — bars closing above prior close / bars closing below
///   DriftH1, DriftH2 — DriftCAGR computed on first/second halves separately (drift stability)
///   RecentDrift365   — DriftCAGR over the last 365 bars (current regime)
///
/// Usage:  StrategyLab profile [--snapshots dir]   (defaults to ../strategy-lab-data)
/// </summary>
public static class ProfileCommand
{
    public static int Run(string snapshotDir)
    {
        if (!Directory.Exists(snapshotDir))
        {
            Console.Error.WriteLine($"Snapshot dir not found: {snapshotDir}");
            return 1;
        }

        // Only OHLCV daily snapshots — skip cross-series, intraday, and weekly
        var files = Directory.GetFiles(snapshotDir, "bitstamp_*_1d.json")
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            Console.Error.WriteLine("No bitstamp_*_1d.json snapshots found.");
            return 1;
        }

        var rows = new List<Profile>();
        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            if (snap.Bars.Count < 400) continue;
            rows.Add(Compute(snap));
        }

        // Print table
        Console.WriteLine();
        Console.WriteLine("Asset Personality Profile (daily snapshots, full history)");
        Console.WriteLine(new string('=', 132));
        Console.WriteLine($"{"Asset",-10} {"Bars",6} {"Years",6}  {"CAGR",8} {"AnnVol",8} {"Sharpe",8}  {"MaxDD",8} {"%InDD",7} {"%>SMA200",9} {"Up/Dn",7}  {"DriftH1",9} {"DriftH2",9} {"Drift365",9}");
        Console.WriteLine(new string('-', 132));
        foreach (var r in rows.OrderByDescending(r => r.Sharpe))
        {
            Console.WriteLine(
                $"{r.Asset,-10} {r.Bars,6} {r.Years,6:0.0}  " +
                $"{r.CAGR,7:+0.0%;-0.0%;0.0%} {r.AnnVol,7:0.0%} {r.Sharpe,8:+0.00;-0.00;0.00}  " +
                $"{r.MaxDD,7:0.0%} {r.PctInDD,6:0.0%} {r.PctAboveSma200,8:0.0%} {r.UpDownRatio,7:0.00}  " +
                $"{r.DriftH1,8:+0.0%;-0.0%;0.0%} {r.DriftH2,8:+0.0%;-0.0%;0.0%} {r.Drift365,8:+0.0%;-0.0%;0.0%}");
        }
        Console.WriteLine();
        Console.WriteLine("Reading the table:");
        Console.WriteLine("  Sharpe < 0.5 OR negative DriftH2: candidates for short-friendly mechanics.");
        Console.WriteLine("  PctAboveSMA200 < 50% confirms persistent below-trend regime.");
        Console.WriteLine("  DriftH1 vs DriftH2 disagreement: regime change — beware single-half curve fits.");
        Console.WriteLine();
        return 0;
    }

    private static Profile Compute(SnapshotFile snap)
    {
        var bars = snap.Bars;
        int n = bars.Count;
        // Asset short name from symbol (BTC/USDT -> BTC)
        string asset = snap.Symbol.Split('/')[0];
        double years = (bars[^1].Date - bars[0].Date).TotalDays / 365.25;

        // Log returns
        var logRet = new double[n - 1];
        for (int i = 1; i < n; i++)
            logRet[i - 1] = Math.Log((double)bars[i].Close / (double)bars[i - 1].Close);

        // Annualized stats (assume 365 trading bars/year for crypto daily)
        double meanRet = logRet.Average();
        double varRet = logRet.Sum(x => (x - meanRet) * (x - meanRet)) / (logRet.Length - 1);
        double stdRet = Math.Sqrt(varRet);
        double annVol = stdRet * Math.Sqrt(365.0);
        double cagr = Math.Exp(meanRet * 365.0) - 1.0;
        double sharpe = annVol > 0 ? (meanRet * 365.0) / annVol : 0;

        // Drawdown stats
        double peak = (double)bars[0].Close;
        double maxDd = 0;
        int inDd = 0;
        foreach (var b in bars)
        {
            double c = (double)b.Close;
            if (c > peak) peak = c;
            double dd = (c - peak) / peak;
            if (dd < maxDd) maxDd = dd;
            if (dd < -0.05) inDd++;  // count >5% drawdown as "in DD"
        }
        double pctInDd = (double)inDd / n;

        // SMA200 trend persistence
        int aboveSma = 0;
        int countedSma = 0;
        var closes = bars.Select(b => (double)b.Close).ToArray();
        for (int i = 199; i < n; i++)
        {
            double sma = 0;
            for (int j = i - 199; j <= i; j++) sma += closes[j];
            sma /= 200.0;
            if (closes[i] > sma) aboveSma++;
            countedSma++;
        }
        double pctAbove = countedSma > 0 ? (double)aboveSma / countedSma : 0;

        // Up/down day ratio
        int up = logRet.Count(r => r > 0);
        int dn = logRet.Count(r => r < 0);
        double udRatio = dn > 0 ? (double)up / dn : 0;

        // Half-period drift comparison
        int mid = n / 2;
        double driftH1 = AnnualizedDrift(closes, 0, mid);
        double driftH2 = AnnualizedDrift(closes, mid, n);
        double drift365 = AnnualizedDrift(closes, Math.Max(0, n - 366), n);

        return new Profile
        {
            Asset = asset, Bars = n, Years = years,
            CAGR = cagr, AnnVol = annVol, Sharpe = sharpe,
            MaxDD = maxDd, PctInDD = pctInDd, PctAboveSma200 = pctAbove,
            UpDownRatio = udRatio, DriftH1 = driftH1, DriftH2 = driftH2, Drift365 = drift365
        };
    }

    private static double AnnualizedDrift(double[] closes, int from, int toExcl)
    {
        if (toExcl - from < 2) return 0;
        double days = toExcl - from - 1;
        if (days <= 0) return 0;
        double tot = Math.Log(closes[toExcl - 1] / closes[from]);
        return Math.Exp(tot * (365.0 / days)) - 1.0;
    }

    private sealed class Profile
    {
        public string Asset = "";
        public int Bars;
        public double Years;
        public double CAGR;
        public double AnnVol;
        public double Sharpe;
        public double MaxDD;
        public double PctInDD;
        public double PctAboveSma200;
        public double UpDownRatio;
        public double DriftH1;
        public double DriftH2;
        public double Drift365;
    }
}
