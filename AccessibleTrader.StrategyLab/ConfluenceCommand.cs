using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Asks whether market STRUCTURE works as context for a signal — the "layer 1 tells you where you
/// are, layer 2 tells you what to do" idea, tested properly.
///
/// <para>
/// The signal is Cipher B's reversal dots. The context is the structure state (uptrend, downtrend,
/// range) from <see cref="SwingStructureAnalyzer"/>, optionally combined with whether the signal
/// fired near a Cipher SR level. If context is worth anything, the SAME signal should perform
/// measurably differently depending on the state it fires in — a long in an established uptrend
/// should beat a long in a downtrend.
/// </para>
///
/// <para>
/// THE CONTROL is a permutation test on the context labels. Realised R-multiples stay attached to
/// their trades and only the STATE labels are reshuffled, thousands of times, to build the null
/// for the between-state spread. This is the right null: it holds the signal's own edge (or lack
/// of one) fixed and isolates the question of whether the context adds information. A simple
/// "uptrend longs made money" table would mostly be measuring that the market went up.
/// </para>
/// </summary>
public static class ConfluenceCommand
{
    private const int HorizonBars = 20;
    private const double RiskAtrFraction = 1.0;
    private const double TargetR = 2.0;

    private sealed record Trade(StructureState State, bool Long, double R, bool NearSrLevel);

    public static async Task<int> RunAsync(string snapshotDir, string? only, string tf, int permutations,
        string bullComponent = CipherBProvider.CompBlue, string bearComponent = CipherBProvider.CompRed,
        double srGateAtr = 0.5)
    {
        Console.WriteLine($"Signal pair: bull='{bullComponent}'  bear='{bearComponent}'  SR gate={srGateAtr} ATR");
        var services = LabHost.Build().Services;
        var engine = services.GetRequiredService<AccessibleTrader.Core.Services.Indicators.IIndicatorEngine>();
        var analyzer = new SwingStructureAnalyzer();

        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var trades = new List<Trade>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 400) continue;

            var parameters = new Dictionary<string, object> { ["__symbol"] = snap.Symbol };

            Dictionary<string, double[]> cipherB, cipherSr;
            try
            {
                cipherB = await engine.CalculateAsync("CIPHER_B", bars, parameters, default);
                cipherSr = await engine.CalculateAsync("CIPHER_SR", bars, parameters, default);
            }
            catch (Exception ex) { Console.Error.WriteLine($"  ! {snap.Symbol}: {ex.Message}"); continue; }

            var structure = analyzer.Analyze(bars, new SwingOptions(Span: 5, MinSwingAtr: 1.0));
            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);

            // EXACT names, and a SYMMETRIC pair. An earlier version matched on fragments and
            // silently paired "Triple Confluence Buy" (the gold dot) against "Bearish Divergence"
            // — two different kinds of signal — which made the short-side comparison meaningless.
            // Oversold/Overbought Crossover are the canonical mirrored Cipher B dots.
            var buys = Exact(cipherB, bullComponent);
            var sells = Exact(cipherB, bearComponent);
            // LOOKAHEAD CORRECTION. CipherSrProvider starts a zone line at pivotBar + 1, but a
            // pivot at bar p is not knowable until p + PivotBars (every one of the next PivotBars
            // bars must fail to exceed it). With AutoScale on — the default — PivotBars is
            // clamp(n/25, 2, 15), so on these series it is 15. Reading the raw array therefore
            // tells the test "price is sitting on a low that WILL turn out to be the low", which
            // is exactly the bias that inflates long results. Shifting the arrays right by
            // PivotBars delays every level to the bar it could first have been known on.
            //
            // NOTE: this affects backtests only. Live, the provider simply cannot see the future
            // bars, so no pivot is emitted early. Any BACKTEST that leafs on Cipher SR zones
            // without this correction is optimistic.
            int srPivotBars = Math.Clamp(bars.Count / 25, 2, 15);
            var srSupport = ShiftRight(Exact(cipherSr, AccessibleTrader.Core.Services.Indicators.CipherSrProvider.CompSupportLine), srPivotBars);
            var srResistance = ShiftRight(Exact(cipherSr, AccessibleTrader.Core.Services.Indicators.CipherSrProvider.CompResistanceLine), srPivotBars);

            if (buys == null || sells == null)
            {
                Console.Error.WriteLine($"  ! {snap.Symbol}: missing '{bullComponent}' or '{bearComponent}' " +
                                        $"(have: {string.Join(", ", cipherB.Keys)})");
                continue;
            }

            AddTrades(trades, bars, atr, structure, buys, isLong: true, srSupport, srResistance, srGateAtr);
            AddTrades(trades, bars, atr, structure, sells, isLong: false, srSupport, srResistance, srGateAtr);
        }

        Report(trades, permutations);
        return 0;
    }

    private static void AddTrades(List<Trade> sink, IReadOnlyList<Ohlcv> bars, double[] atr,
        SwingStructure structure, double[]? signal, bool isLong,
        double[]? srSupport, double[]? srResistance, double srGateAtr)
    {
        if (signal == null) return;

        for (int i = 1; i < bars.Count - HorizonBars - 1; i++)
        {
            if (double.IsNaN(signal[i])) continue;
            double a = atr[i];
            if (double.IsNaN(a) || a <= 0) continue;

            // Enter on the NEXT bar's open — a signal on bar i is only actionable after it closes.
            double entry = bars[i + 1].Open;
            double risk = a * RiskAtrFraction;
            double stop = isLong ? entry - risk : entry + risk;
            double target = isLong ? entry + risk * TargetR : entry - risk * TargetR;

            double r = 0;
            bool resolved = false;
            for (int j = i + 1; j <= i + HorizonBars; j++)
            {
                if (isLong ? bars[j].Low <= stop : bars[j].High >= stop) { r = -1; resolved = true; break; }
                if (isLong ? bars[j].High >= target : bars[j].Low <= target) { r = TargetR; resolved = true; break; }
            }
            if (!resolved)
            {
                double exit = bars[i + HorizonBars].Close;
                r = (isLong ? exit - entry : entry - exit) / risk;
            }

            // "Near an SR level" = within srGateAtr of the relevant carried Cipher SR zone.
            var zone = isLong ? srSupport : srResistance;
            bool near = false;
            if (zone != null && i < zone.Length && !double.IsNaN(zone[i]))
                near = Math.Abs(bars[i].Close - zone[i]) <= a * srGateAtr;

            sink.Add(new Trade(structure.StatePerBar[i], isLong, r, near));
        }
    }

    private static void Report(List<Trade> trades, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine($"===== STRUCTURE AS CONTEXT — {trades.Count:N0} Cipher B signals =====");
        Console.WriteLine($"Entered next open, {TargetR}R target / 1R stop, {HorizonBars}-bar horizon.");
        Console.WriteLine();

        if (trades.Count < 100) { Console.WriteLine("Too few trades."); return; }

        Console.WriteLine($"  {"direction",-10} {"structure",-12} {"n",6} {"win%",6} {"meanR",8}");
        foreach (var dir in new[] { true, false })
        {
            foreach (var st in new[] { StructureState.Uptrend, StructureState.Range, StructureState.Downtrend })
            {
                var g = trades.Where(t => t.Long == dir && t.State == st).ToList();
                if (g.Count < 20) continue;
                Console.WriteLine($"  {(dir ? "long" : "short"),-10} {st,-12} {g.Count,6} " +
                                  $"{g.Count(t => t.R > 0) / (double)g.Count,5:P0} {g.Average(t => t.R),8:+0.000;-0.000;0}");
            }
        }
        Console.WriteLine($"  {"ALL",-10} {"",-12} {trades.Count,6} " +
                          $"{trades.Count(t => t.R > 0) / (double)trades.Count,5:P0} {trades.Average(t => t.R),8:+0.000;-0.000;0}");

        // ── Does the state label carry information? ──────────────────────────
        foreach (var dir in new[] { true, false })
        {
            var withTrend = trades.Where(t => t.Long == dir &&
                t.State == (dir ? StructureState.Uptrend : StructureState.Downtrend)).ToList();
            var against = trades.Where(t => t.Long == dir &&
                t.State == (dir ? StructureState.Downtrend : StructureState.Uptrend)).ToList();
            if (withTrend.Count < 30 || against.Count < 30) continue;

            double observed = withTrend.Average(t => t.R) - against.Average(t => t.R);
            var pool = trades.Where(t => t.Long == dir).Select(t => t.R).ToArray();
            double p = PermutationP(pool, withTrend.Count, against.Count, observed, permutations);

            Console.WriteLine();
            Console.WriteLine($"  {(dir ? "LONGS" : "SHORTS")}: with-structure {withTrend.Average(t => t.R):+0.000;-0.000;0}R " +
                              $"(n={withTrend.Count}) vs against-structure {against.Average(t => t.R):+0.000;-0.000;0}R (n={against.Count})");
            Console.WriteLine($"    gap {observed:+0.000;-0.000;0}R, permutation p = {p:0.0000}" +
                              (p <= 0.05 ? "  → structure carries information" : "  → indistinguishable from random labels"));
        }

        // ── Does adding Cipher SR proximity on top add anything? ─────────────
        var near = trades.Where(t => t.NearSrLevel).ToList();
        var far = trades.Where(t => !t.NearSrLevel).ToList();
        Console.WriteLine();
        Console.WriteLine($"  SR sample: near={near.Count}, away={far.Count}");
        if (near.Count >= 30 && far.Count >= 30)
        {
            double observed = near.Average(t => t.R) - far.Average(t => t.R);
            double p = PermutationP(trades.Select(t => t.R).ToArray(), near.Count, far.Count, observed, permutations);
            Console.WriteLine();
            Console.WriteLine($"  CIPHER SR PROXIMITY: near {near.Average(t => t.R):+0.000;-0.000;0}R (n={near.Count}) " +
                              $"vs away {far.Average(t => t.R):+0.000;-0.000;0}R (n={far.Count})");
            Console.WriteLine($"    gap {observed:+0.000;-0.000;0}R, permutation p = {p:0.0000}" +
                              (p <= 0.05 ? "  → SR proximity carries information" : "  → indistinguishable from random labels"));
        }

        // ── The full three-way stack ─────────────────────────────────────────
        var stacked = trades.Where(t => t.NearSrLevel &&
            t.State == (t.Long ? StructureState.Uptrend : StructureState.Downtrend)).ToList();
        if (stacked.Count >= 20)
        {
            Console.WriteLine();
            Console.WriteLine($"  FULL STACK (Cipher B + with-structure + at an SR level): n={stacked.Count}, " +
                              $"win {stacked.Count(t => t.R > 0) / (double)stacked.Count:P0}, mean {stacked.Average(t => t.R):+0.000;-0.000;0}R");
            Console.WriteLine($"  Baseline (all Cipher B signals): mean {trades.Average(t => t.R):+0.000;-0.000;0}R");
        }
    }

    /// <summary>
    /// Two-sample permutation test — see <see cref="LabStats.PermutationP(double[], int, int, double, int, int, int?, out int)"/>. The seed lives here,
    /// not in the shared helper, because it is this command's research parameter.
    /// </summary>
    private static double PermutationP(double[] pool, int nA, int nB, double observed, int runs) =>
        LabStats.PermutationP(pool, nA, nB, observed, runs, seed: 4242);
    /// <summary>
    /// Delays a series by <paramref name="lag"/> bars, filling the head with NaN. Used to undo an
    /// indicator's confirmation lookahead so a backtest only sees what was knowable at the time.
    /// </summary>
    private static double[]? ShiftRight(double[]? src, int lag)
    {
        if (src == null || lag <= 0) return src;
        var dst = new double[src.Length];
        for (int i = 0; i < src.Length; i++) dst[i] = i < lag ? double.NaN : src[i - lag];
        return dst;
    }

    /// <summary>Exact component lookup — no fragment matching, so a rename fails loudly.</summary>
    private static double[]? Exact(Dictionary<string, double[]> data, string name) =>
        data.TryGetValue(name, out var arr) ? arr : null;
}
