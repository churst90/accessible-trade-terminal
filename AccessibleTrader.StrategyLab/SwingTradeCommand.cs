using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Answers "would I have made money buying every swing low and selling every swing high?"
///
/// <para>
/// THE TRAP THIS EXISTS TO EXPOSE. A swing marker is DRAWN on the pivot bar, but a pivot cannot
/// be identified until <c>Span</c> further bars have failed to exceed it. The mark therefore
/// appears in a place you could never have traded — by the time it shows up, price has already
/// moved Span bars away from it. Reading a chart full of markers sitting exactly on the lows is
/// the single most seductive illusion in technical analysis, and the only way to measure the
/// difference is to run both versions side by side.
/// </para>
///
/// <para>
/// So this runs three books over the same swings:
///   ORACLE   — fills at the pivot bar's price. Impossible. Included only to size the illusion.
///   HONEST   — fills at the close of the bar where the pivot could first be KNOWN.
///   HOLD     — buy the first bar, sell the last. The benchmark that matters, because a strategy
///              that underperforms doing nothing is worse than nothing once costs are counted.
/// </para>
/// </summary>
public static class SwingTradeCommand
{
    private sealed record Result(string Asset, int Trades, double OracleReturn, double HonestReturn,
        double HoldReturn, double HonestWinRate);

    public static Task<int> RunAsync(string snapshotDir, string? only, string tf,
        int span, double minSwingAtr, double costBps)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var analyzer = new SwingStructureAnalyzer();
        var results = new List<Result>();
        double cost = costBps / 10000.0;

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 200) continue;

            var swings = CausalSwings(bars, span, minSwingAtr);
            if (swings.Count < 4) continue;

            // Alternate low → high → low → high. Each completed pair is one round trip.
            double oracle = 1.0, honest = 1.0;
            int trades = 0, wins = 0;
            int? entryOracleIdx = null, entryHonestIdx = null;

            foreach (var sw in swings)
            {
                if (!sw.IsHigh)
                {
                    // A low: open a position, if flat.
                    if (entryOracleIdx == null)
                    {
                        entryOracleIdx = sw.BarIndex;
                        entryHonestIdx = sw.ConfirmedAtIndex;
                    }
                }
                else if (entryOracleIdx != null)
                {
                    // A high: close it.
                    double oIn = bars[entryOracleIdx.Value].Low;      // the pivot price itself
                    double oOut = bars[sw.BarIndex].High;
                    oracle *= (oOut / oIn) * (1 - cost) * (1 - cost);

                    double hIn = bars[entryHonestIdx!.Value].Close;   // first knowable bar
                    double hOut = bars[Math.Min(sw.ConfirmedAtIndex, bars.Count - 1)].Close;
                    double leg = (hOut / hIn) * (1 - cost) * (1 - cost);
                    honest *= leg;
                    if (leg > 1) wins++;

                    trades++;
                    entryOracleIdx = null;
                    entryHonestIdx = null;
                }
            }

            if (trades < 3) continue;
            double hold = bars[^1].Close / bars[0].Close;
            results.Add(new Result(snap.Symbol, trades, (oracle - 1) * 100, (honest - 1) * 100,
                (hold - 1) * 100, trades > 0 ? wins / (double)trades * 100 : 0));
        }

        Report(results, tf, span, minSwingAtr, costBps);
        return Task.FromResult(0);
    }

    private static void Report(List<Result> rows, string tf, int span, double minAtr, double costBps)
    {
        Console.WriteLine();
        Console.WriteLine($"===== SWING LOW → SWING HIGH ({tf}, span {span}, min {minAtr} ATR, {costBps:0.#} bps/side) =====");
        Console.WriteLine("ORACLE fills at the pivot price (impossible). HONEST fills where the pivot could");
        Console.WriteLine("first be known. HOLD is buy-and-hold over the same bars. Total return, %.");
        Console.WriteLine();

        if (rows.Count == 0) { Console.WriteLine("No asset produced enough trades."); return; }

        Console.WriteLine($"  {"asset",-14} {"trades",7} {"ORACLE %",12} {"HONEST %",12} {"HOLD %",12} {"win%",7}");
        foreach (var r in rows.OrderByDescending(r => r.HonestReturn))
            Console.WriteLine($"  {r.Asset,-14} {r.Trades,7} {r.OracleReturn,12:+0;-0;0} {r.HonestReturn,12:+0;-0;0} " +
                              $"{r.HoldReturn,12:+0;-0;0} {r.HonestWinRate,6:0}%");

        Console.WriteLine();
        Console.WriteLine($"  Median ORACLE {Median(rows.Select(r => r.OracleReturn)):+0;-0;0}%   " +
                          $"HONEST {Median(rows.Select(r => r.HonestReturn)):+0;-0;0}%   " +
                          $"HOLD {Median(rows.Select(r => r.HoldReturn)):+0;-0;0}%");
        int beatHold = rows.Count(r => r.HonestReturn > r.HoldReturn);
        Console.WriteLine($"  HONEST beat buy-and-hold on {beatHold}/{rows.Count} assets.");
        Console.WriteLine($"  Median cost of not being able to see the future: " +
                          $"{Median(rows.Select(r => r.OracleReturn)) - Median(rows.Select(r => r.HonestReturn)):0} points of return.");
    }

    /// <summary>
    /// Swing pivots detected the way a live trader would see them, in order, with no revision.
    ///
    /// <para>
    /// <see cref="SwingStructureAnalyzer"/> is right for DESCRIBING history — including a pass
    /// that replaces an already-recorded pivot when a later, more extreme one of the same kind
    /// arrives. That is exactly what you want when narrating a chart, and exactly what you must
    /// not have in a backtest: in real time you would already have traded the first one. Running
    /// the descriptive analyzer through a P&amp;L loop produced returns in the millions of percent,
    /// which is what that revision is worth when you let it leak.
    /// </para>
    ///
    /// <para>
    /// Here a pivot at bar p is emitted at p + span, only if it was the extreme of
    /// [p - span, p + span], and once emitted it is never revised. Alternation and the ATR
    /// significance floor are applied ONLINE against what had already been emitted.
    /// </para>
    /// </summary>
    private static List<SwingPoint> CausalSwings(IReadOnlyList<Ohlcv> bars, int span, double minSwingAtr)
    {
        var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);
        var emitted = new List<SwingPoint>();

        for (int confirmAt = span * 2; confirmAt < bars.Count; confirmAt++)
        {
            int p = confirmAt - span;

            bool isHigh = true, isLow = true;
            for (int j = p - span; j <= p + span && (isHigh || isLow); j++)
            {
                if (j == p || j < 0) continue;
                if (bars[j].High >= bars[p].High) isHigh = false;
                if (bars[j].Low <= bars[p].Low) isLow = false;
            }
            if (!isHigh && !isLow) continue;

            bool high = isHigh;
            double price = high ? bars[p].High : bars[p].Low;

            if (emitted.Count > 0)
            {
                var last = emitted[^1];
                // Same kind twice running: in real time the first one already happened, so the
                // second is simply skipped rather than retroactively replacing it.
                if (last.IsHigh == high) continue;

                double a = atr[Math.Min(p, atr.Length - 1)];
                if (!double.IsNaN(a) && a > 0 && Math.Abs(price - last.Price) < a * minSwingAtr) continue;
            }
            else if (high)
            {
                continue; // start the sequence on a low so every pair is a complete round trip
            }

            emitted.Add(new SwingPoint(p, confirmAt, high, price, SwingLabel.Initial, bars[p].Date));
        }

        return emitted;
    }

    private static double Median(IEnumerable<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        if (s.Count == 0) return double.NaN;
        int m = s.Count / 2;
        return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2;
    }
}
