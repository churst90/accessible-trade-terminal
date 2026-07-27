using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Turns the POC-deviation finding into the question that actually matters: after costs, is
/// there anything left?
///
/// <para>
/// Reports per-tier forward returns so the right NUMBER of tiers can be chosen from evidence
/// rather than taste, and then simulates the scale-in plan directly — enter at tier k below the
/// POC, exit at the POC or at tier k above — charging a round-trip cost every time.
/// </para>
///
/// <para>
/// Returns are expressed in PERCENT, not ATR. ATR normalisation is right for comparing assets
/// and eras, but it hides whether an edge survives a spread, because costs are paid in percent.
/// A 0.12 ATR edge sounds convincing until the instrument's ATR is 0.8% and the round trip is
/// 0.05%.
/// </para>
/// </summary>
public static class PocTierCommand
{
    private const int BinCount = 50;
    private const int RebuildEvery = 5;

    private sealed record Obs(double DeviationVa, double FwdPct, string Asset);

    /// <param name="anchor">
    /// How the deviation is scaled.
    ///   "va"     — distance from the fast POC in value-area widths (the original).
    ///   "spread" — distance from the SLOW POC, scaled by the gap between the fast and slow
    ///              POCs. This is the multi-timeframe idea: the band's width is set by how far
    ///              short-term value has already migrated from long-term value, so the ruler
    ///              itself adapts instead of being a fixed window's value area.
    ///   "slow"   — distance from the slow POC in the slow window's value-area widths.
    /// </param>
    public static Task<int> RunAsync(string snapshotDir, string? only, string tf,
        int window, int forwardBars, double costBps, int tiers, string anchor = "va", int slowMult = 4)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var profiles = new ProfileService();
        var obs = new List<Obs>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < window + forwardBars + 50) continue;

            double poc = double.NaN, vaLow = double.NaN, vaHigh = double.NaN;
            double slowPoc = double.NaN, slowLow = double.NaN, slowHigh = double.NaN;
            int slowWindow = window * slowMult;
            int start = Math.Max(window, anchor == "va" ? window : slowWindow);

            for (int i = start; i < bars.Count - forwardBars; i++)
            {
                if ((i - start) % RebuildEvery == 0)
                {
                    var slice = new List<Ohlcv>(window);
                    for (int k = i - window; k < i; k++) slice.Add(bars[k]);
                    var bins = profiles.CalculateVolumeProfile(slice, BinCount);
                    if (bins.Count == 0) { poc = double.NaN; continue; }
                    poc = bins.FirstOrDefault(b => b.IsPOC)?.PriceMid ?? double.NaN;
                    var va = bins.Where(b => b.IsValueArea).ToList();
                    vaLow = va.Count > 0 ? va.Min(b => b.PriceLow) : double.NaN;
                    vaHigh = va.Count > 0 ? va.Max(b => b.PriceHigh) : double.NaN;

                    if (anchor != "va" && i >= slowWindow)
                    {
                        var slowSlice = new List<Ohlcv>(slowWindow);
                        for (int k = i - slowWindow; k < i; k++) slowSlice.Add(bars[k]);
                        var slowBins = profiles.CalculateVolumeProfile(slowSlice, BinCount);
                        slowPoc = slowBins.FirstOrDefault(b => b.IsPOC)?.PriceMid ?? double.NaN;
                        var sva = slowBins.Where(b => b.IsValueArea).ToList();
                        slowLow = sva.Count > 0 ? sva.Min(b => b.PriceLow) : double.NaN;
                        slowHigh = sva.Count > 0 ? sva.Max(b => b.PriceHigh) : double.NaN;
                    }
                }

                if (double.IsNaN(poc) || double.IsNaN(vaLow) || double.IsNaN(vaHigh)) continue;
                double vaWidth = vaHigh - vaLow;
                if (vaWidth <= 0 || bars[i].Close <= 0) continue;

                double dev;
                if (anchor == "spread")
                {
                    if (double.IsNaN(slowPoc)) continue;
                    // The ruler is the migration of value itself. A floor keeps a moment of
                    // perfect agreement between the two POCs from dividing by ~zero.
                    double spread = Math.Abs(poc - slowPoc);
                    double scale = Math.Max(spread, vaWidth * 0.25);
                    dev = (bars[i].Close - slowPoc) / scale;
                }
                else if (anchor == "slow")
                {
                    if (double.IsNaN(slowPoc) || double.IsNaN(slowLow) || double.IsNaN(slowHigh)) continue;
                    double slowWidth = slowHigh - slowLow;
                    if (slowWidth <= 0) continue;
                    dev = (bars[i].Close - slowPoc) / slowWidth;
                }
                else
                {
                    dev = (bars[i].Close - poc) / vaWidth;
                }
                if (Math.Abs(dev) > 10) continue;

                double fwdPct = (bars[i + forwardBars].Close - bars[i].Close) / bars[i].Close * 100.0;
                obs.Add(new Obs(dev, fwdPct, snap.Symbol));
            }
        }

        Report(obs, tf, forwardBars, costBps, tiers, anchor);
        return Task.FromResult(0);
    }

    private static void Report(List<Obs> obs, string tf, int forwardBars, double costBps, int tiers, string anchor)
    {
        Console.WriteLine();
        Console.WriteLine($"===== POC TIERS ({tf}, anchor={anchor}, {forwardBars}-bar hold, {tiers} tiers, {costBps:0.#} bps) =====");
        Console.WriteLine($"{obs.Count:N0} observations. Forward returns in PERCENT so costs are comparable.");
        Console.WriteLine();

        if (obs.Count < 1000) { Console.WriteLine("Too few observations."); return; }

        // Tier boundaries in value-area widths, evenly spaced out to 2 VA.
        var edges = Enumerable.Range(1, tiers).Select(k => 2.0 * k / tiers).ToArray();

        Console.WriteLine($"  {"tier",5} {"band (VA)",16} {"side",6} {"n",8} {"mean %",9} {"win%",6} {"net of cost",12}");
        double cost = costBps / 100.0; // bps → percent

        for (int k = tiers - 1; k >= 0; k--)
        {
            double lo = edges[k], hi = k + 1 < tiers ? edges[k + 1] : 999;

            var below = obs.Where(o => -o.DeviationVa >= lo && -o.DeviationVa < hi).ToList();
            var above = obs.Where(o => o.DeviationVa >= lo && o.DeviationVa < hi).ToList();

            if (below.Count > 50)
                Console.WriteLine($"  {k + 1,5} {"-" + lo.ToString("0.0") + " to -" + (hi > 900 ? "inf" : hi.ToString("0.0")),16} " +
                                  $"{"BUY",6} {below.Count,8} {below.Average(o => o.FwdPct),9:+0.000;-0.000} " +
                                  $"{below.Count(o => o.FwdPct > 0) / (double)below.Count,5:P0} {below.Average(o => o.FwdPct) - cost,12:+0.000;-0.000}");

            if (above.Count > 50)
                Console.WriteLine($"  {k + 1,5} {"+" + lo.ToString("0.0") + " to +" + (hi > 900 ? "inf" : hi.ToString("0.0")),16} " +
                                  $"{"SELL",6} {above.Count,8} {above.Average(o => o.FwdPct),9:+0.000;-0.000} " +
                                  $"{above.Count(o => o.FwdPct > 0) / (double)above.Count,5:P0} " +
                                  $"{-above.Average(o => o.FwdPct) - cost,12:+0.000;-0.000}");
        }

        // ── The question actually asked: trade ONLY the outermost tier ────────
        double outer = edges[^1];
        var buys = obs.Where(o => o.DeviationVa <= -outer).ToList();
        var sells = obs.Where(o => o.DeviationVa >= outer).ToList();

        Console.WriteLine();
        Console.WriteLine($"  OUTERMOST TIER ONLY (|deviation| >= {outer:0.0} VA):");

        double buyNet = buys.Count > 0 ? buys.Average(o => o.FwdPct) - cost : double.NaN;
        double sellNet = sells.Count > 0 ? -sells.Average(o => o.FwdPct) - cost : double.NaN;

        Console.WriteLine($"    long side : n={buys.Count,6}  gross {(buys.Count > 0 ? buys.Average(o => o.FwdPct) : 0),7:+0.000;-0.000}%  net {buyNet,7:+0.000;-0.000}%");
        Console.WriteLine($"    short side: n={sells.Count,6}  gross {(sells.Count > 0 ? -sells.Average(o => o.FwdPct) : 0),7:+0.000;-0.000}%  net {sellNet,7:+0.000;-0.000}%");

        // Long-only is the honest headline: it is what a scale-in/scale-out plan on an
        // owned position actually does, and it avoids borrow costs entirely.
        if (buys.Count > 0)
        {
            // Trades per year per symbol, assuming ~252 bars/yr and one position per signal.
            double perYearPerSymbol = buys.Count / (double)obs.Select(o => o.Asset).Distinct().Count()
                                      / (obs.Count / (double)obs.Select(o => o.Asset).Distinct().Count() / 252.0);
            Console.WriteLine();
            Console.WriteLine($"    long-only, per symbol: ~{perYearPerSymbol:0.0} signals/year at {buyNet:+0.000;-0.000}% net each");
            Console.WriteLine($"    → naive annual contribution if always one unit deployed: {perYearPerSymbol * buyNet:+0.00;-0.00}%");
            Console.WriteLine("    (Not a return figure: overlapping signals, capital sitting idle between them,");
            Console.WriteLine("     and position sizing are all ignored. It bounds the order of magnitude only.)");
        }

        // ── Does adding tiers actually buy anything? ─────────────────────────
        Console.WriteLine();
        Console.WriteLine("  MONOTONICITY CHECK — does a deeper tier really pay more?");
        for (int k = 0; k < tiers; k++)
        {
            double lo = edges[k], hi = k + 1 < tiers ? edges[k + 1] : 999;
            var below = obs.Where(o => -o.DeviationVa >= lo && -o.DeviationVa < hi).ToList();
            if (below.Count > 50)
                Console.WriteLine($"    buy tier {k + 1}: {below.Average(o => o.FwdPct),7:+0.000;-0.000}%  (n={below.Count})");
        }
    }
}
