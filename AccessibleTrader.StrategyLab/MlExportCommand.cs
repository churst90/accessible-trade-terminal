using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;
using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// ML feature-export (round 14, 2026-06-14). Produces a leak-aware training dataset for the
/// "confidence model" experiment: one row per bar, with PROVABLY-CAUSAL continuous indicator
/// features (trailing-window oscillators + regime + volatility — no pivot/divergence/SR markers,
/// which we confirmed stamp at the pivot bar using future bars) and a forward-looking
/// triple-barrier LABEL (the only thing allowed to see the future, because it is the target).
///
/// Label (long perspective, matching the suite's risk style): from each bar, walk forward up to
/// Horizon bars. ATR(14)-scaled barriers up=+1.5·ATR (target), down=-1.0·ATR (stop). win=1 if
/// the target is touched before the stop; win=0 if the stop is touched first; timeouts (neither
/// within Horizon) are labelled by the sign of the horizon return. The model learns P(win) — the
/// calibrated buy-confidence the user wants to tint dots / pitch earcons by.
///
/// One combined CSV across all assets/timeframes (asset + tf + date columns) so Python can do a
/// strictly chronological walk-forward split and per-asset slicing.
/// </summary>
public static class MlExportCommand
{
    private const int Warmup = 400;        // enough for SMA200 + VOL_REGIME long window to stabilize
    private const int Horizon = 20;        // forward bars for the triple barrier
    private const double UpAtr = 1.5;      // target = +1.5 ATR
    private const double DownAtr = 1.0;    // stop   = -1.0 ATR
    private const int AtrPeriod = 14;

    // Provably-causal continuous features only. (code, component, outName).
    private static readonly (string Code, string Comp, string Name)[] Features =
    {
        ("CIPHER_B", "Wave Trend",       "wt1"),
        ("CIPHER_B", "Wave Trend 2",     "wt2"),
        ("CIPHER_B", "WT Histogram",     "wt_hist"),
        ("CIPHER_B", "Money Flow Wave",  "mfw"),
        ("CIPHER_B", "Anchor Wave",      "anchor"),
        ("CIPHER_B", "Anchor Wave 2",    "anchor2"),
        ("CIPHER_C", "Cycle Sine",       "c_sine"),
        ("CIPHER_C", "Lead Sine",        "c_lead"),
        ("HURST",    "Hurst",            "hurst"),
        ("VOL_REGIME", "VolRatio",       "vol_ratio"),
        ("VOL_REGIME", "VolPercentile",  "vol_pct"),
        ("VOL_REGIME", "VolState",       "vol_state"),
        ("REGIME",   "RegimeState",      "regime"),
    };

    // Causal buy-signal markers (WT cross / oversold cross / gold are computed from current+prior
    // WT values, NOT pivot-confirmed — so they are leak-free, unlike divergence/SR). Emitted as
    // fired-flags so Python can (a) use them as features and (b) filter to signal bars for the
    // meta-model test ("confidence WHEN a setup fires").
    private static readonly (string Code, string Comp, string Name)[] Markers =
    {
        ("CIPHER_B", "WaveTrend Cross Bull", "sig_wtx"),
        ("CIPHER_B", "Oversold Crossover",   "sig_blue"),
        ("CIPHER_B", "Triple Confluence Buy","sig_gold"),
    };

    public static async Task<int> RunAsync(string snapshotDir, string? only, string tf, string outCsv)
    {
        if (!Directory.Exists(snapshotDir)) { Console.Error.WriteLine($"No dir: {snapshotDir}"); return 1; }
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();
        if (only != null)
        {
            var terms = only.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLowerInvariant()).ToArray();
            files = files.Where(f => terms.Any(t => Path.GetFileName(f).ToLowerInvariant().Contains(t))).ToList();
        }
        if (files.Count == 0) { Console.Error.WriteLine("No matching snapshots."); return 1; }

        var header = new List<string> { "asset", "tf", "date", "close" };
        header.AddRange(new[] { "ret1", "ret5", "atr_pct", "dist_sma_pct", "range_pct" });
        header.AddRange(Features.Select(f => f.Name));
        header.AddRange(Markers.Select(m => m.Name));
        header.AddRange(new[] { "win", "tb_outcome", "fwd_ret_h" });

        using var writer = new StreamWriter(outCsv, append: false);
        writer.WriteLine(string.Join(",", header));

        int totalRows = 0;
        foreach (var file in files)
        {
            var snap = SnapshotCommand.Load(file);
            var bars = snap.Bars;
            if (bars.Count < Warmup + Horizon + 50)
            {
                Console.WriteLine($"  - {snap.Symbol}: {bars.Count} bars, too short, skipped.");
                continue;
            }
            Console.Write($"  · {snap.Symbol,-10} {bars.Count,6} bars … ");
            var host = LabHost.Build();
            var honest = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase)
            { ["CIPHER_B"] = new Dictionary<string, object> { ["DivergenceConfirmLag"] = 1.0 } };
            var state = await WorkspaceFactory.BuildAsync(host.Services, snap, parameterOverrides: honest);

            var cols = Features.Select(f => ReadComponent(state, f.Code, f.Comp)).ToArray();
            var markerCols = Markers.Select(m => ReadComponent(state, m.Code, m.Comp)).ToArray();
            var atr = WilderAtr(bars, AtrPeriod);
            var sma200 = Sma(bars, 200);

            int rows = 0;
            int last = bars.Count - Horizon - 1; // need Horizon forward bars for the label
            for (int i = Warmup; i <= last; i++)
            {
                double a = atr[i];
                if (double.IsNaN(a) || a <= 0) continue;

                // Label: triple barrier from bar i (entry at close[i]).
                double entry = bars[i].Close;
                double up = entry + UpAtr * a, down = entry - DownAtr * a;
                int outcome = 2; // 0 stop, 1 target, 2 timeout
                for (int j = i + 1; j <= i + Horizon; j++)
                {
                    bool hitUp = bars[j].High >= up, hitDown = bars[j].Low <= down;
                    if (hitUp && hitDown) { outcome = 0; break; }  // both in one bar → conservative: stop
                    if (hitDown) { outcome = 0; break; }
                    if (hitUp)   { outcome = 1; break; }
                }
                double fwdRet = Math.Log(bars[i + Horizon].Close / entry);
                int win = outcome == 1 ? 1 : (outcome == 0 ? 0 : (fwdRet > 0 ? 1 : 0));

                // Causal engineered features (all use data ≤ i).
                double ret1 = i >= 1 ? Math.Log(entry / bars[i - 1].Close) : 0;
                double ret5 = i >= 5 ? Math.Log(entry / bars[i - 5].Close) : 0;
                double atrPct = a / entry;
                double distSma = !double.IsNaN(sma200[i]) && sma200[i] > 0 ? (entry - sma200[i]) / entry : double.NaN;
                double rangePct = (bars[i].High - bars[i].Low) / entry;

                var row = new List<string>
                {
                    Csv(snap.Symbol), tf, bars[i].Date.ToString("yyyy-MM-dd"), F(entry),
                    F(ret1), F(ret5), F(atrPct), F(distSma), F(rangePct)
                };
                foreach (var c in cols) row.Add(c != null && i < c.Length ? F(c[i]) : "");
                // Markers → fired-flag (1 if non-NaN at bar i, else 0). Causal by construction.
                foreach (var m in markerCols) row.Add(m != null && i < m.Length && !double.IsNaN(m[i]) ? "1" : "0");
                row.Add(win.ToString());
                row.Add(outcome.ToString());
                row.Add(F(fwdRet));
                writer.WriteLine(string.Join(",", row));
                rows++;
            }
            totalRows += rows;
            Console.WriteLine($"{rows} rows");
        }

        writer.Flush();
        Console.WriteLine($"\nWrote {totalRows} rows → {outCsv}");
        Console.WriteLine($"Features ({Features.Length} causal): {string.Join(", ", Features.Select(f => f.Name))}");
        Console.WriteLine($"Label: triple-barrier long, up={UpAtr}·ATR target / down={DownAtr}·ATR stop, horizon={Horizon} bars.");
        return 0;
    }

    private static double[]? ReadComponent(WorkspaceState state, string code, string comp)
    {
        var s = state.ActiveSeries.FirstOrDefault(x => x.Name == code);
        if (s == null) return null;
        try { return s.GetComponentData(comp); } catch { return null; }
    }

    private static double[] WilderAtr(IReadOnlyList<Ohlcv> bars, int period)
    {
        int n = bars.Count;
        var atr = new double[n];
        Array.Fill(atr, double.NaN);
        if (n < period + 1) return atr;
        double sum = 0;
        for (int i = 1; i <= period; i++) sum += Tr(bars, i);
        double prev = sum / period;
        atr[period] = prev;
        for (int i = period + 1; i < n; i++)
        {
            prev = (prev * (period - 1) + Tr(bars, i)) / period;
            atr[i] = prev;
        }
        return atr;
    }

    private static double Tr(IReadOnlyList<Ohlcv> b, int i)
    {
        double h = b[i].High, l = b[i].Low, pc = b[i - 1].Close;
        return Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
    }

    private static double[] Sma(IReadOnlyList<Ohlcv> bars, int period)
    {
        int n = bars.Count;
        var sma = new double[n];
        Array.Fill(sma, double.NaN);
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += bars[i].Close;
            if (i >= period) sum -= bars[i - period].Close;
            if (i >= period - 1) sma[i] = sum / period;
        }
        return sma;
    }

    private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    private static string Csv(string s) => s.Replace(",", "");
}
