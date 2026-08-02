using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// The ladder: always in the market in whichever direction price is moving, with a trailing stop.
///
/// <para>
/// THE IDEA, as posed: "if price goes in a certain direction for a number of clicks it opens a
/// trade, so long as price goes in that direction, with a trailing stop — so no matter the
/// direction there is always a trade open so long as there is trend."
/// </para>
///
/// <para>
/// FORMALISED. A click is a fixed fraction of ATR, so the rung spacing breathes with volatility
/// instead of being a currency amount that means different things in 2015 and 2026. From a reference
/// price, N consecutive clicks in one direction opens a position that way. A trailing stop of K
/// clicks rides behind it. When the stop is hit, the reference resets to that price and the ladder
/// starts counting again — so the system is flat only between a stop-out and the next N-click move,
/// and it will happily reverse.
/// </para>
///
/// <para>
/// WHAT THE PRIOR SAYS THIS WILL DO. The registry already holds a control-tested edge saying the
/// trend-following FAMILY works in crypto and that its specific parameters carry no information —
/// randomly drawn parameters beat optimised ones out of sample. A ladder is a member of that family,
/// so the honest prediction is: positive in crypto, weak elsewhere, and no better than any other
/// trend rule. The interesting question is not "does it work" but the two parts that are actually
/// new — whether ALWAYS being in the market beats being long-only, and whether the short side pays
/// at all, given five separate symmetric-short attempts in this project have failed.
/// </para>
///
/// <para>THREE CONTROLS.</para>
/// <list type="number">
///   <item><b>Buy and hold</b> — the thing any long-biased system in a rising asset must beat.</item>
///   <item><b>The same ladder, long only</b> — isolates what the short side contributes.</item>
///   <item>
///     <b>Random parameters</b>: the ladder run with N and K drawn at random per trial. If the
///     tuned parameters do not beat the random ones, the parameters carry no information — this is
///     the control that decided the walk-forward study and it applies here for the same reason.
///   </item>
/// </list>
/// </summary>
public static class LadderCommand
{
    private sealed record Book(double Equity, double MaxDd, int Trades, double WinRate, double ExposurePct);

    public static int Run(string snapshotDir, string tf, int clicks, int trailClicks, double clickAtr, int randomTrials)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_") && !Path.GetFileName(f).StartsWith("events_")
                     && !Path.GetFileName(f).StartsWith("fred_"))
            .OrderBy(f => f).ToList();

        Console.WriteLine();
        Console.WriteLine("═════ THE LADDER — always in the trend's direction ═════");
        Console.WriteLine($"click = {clickAtr} ATR · {clicks} clicks to open · {trailClicks}-click trailing stop · {tf}");
        Console.WriteLine();
        Console.WriteLine($"  {"symbol",-12}{"class",-9}{"ladder",10}{"long-only",11}{"hold",10}{"random",10}{"trades",8}{"in mkt",8}{"maxDD",8}");

        var rows = new List<(string Cls, double Lad, double Long, double Hold, double Rand)>();

        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            if (snap.Bars.Count < 600) continue;
            var bars = snap.Bars.ToArray();
            var name = Path.GetFileNameWithoutExtension(f).Split('_');
            string sym = string.Join('_', name[1..^1]);
            string cls = name[0] is "bitstamp" or "mexc" ? "crypto" : "equity";

            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars, 14);
            var both = Simulate(bars, atr, clicks, trailClicks, clickAtr, allowShort: true);
            var longOnly = Simulate(bars, atr, clicks, trailClicks, clickAtr, allowShort: false);
            double hold = bars[^1].Close / bars[200].Close;

            // Random-parameter arm: same machinery, N and K drawn per trial. Seeded from the file
            // name so a re-run reproduces — a control that resamples is not a control.
            var rng = new Random(StableSeed(f));
            double randMean = 0;
            for (int t = 0; t < randomTrials; t++)
            {
                int n = 1 + rng.Next(6);          // 1..6 clicks to open
                int k = 1 + rng.Next(6);          // 1..6 click trail
                double c = 0.25 + rng.NextDouble() * 1.25;
                randMean += Simulate(bars, atr, n, k, c, allowShort: true).Equity;
            }
            randMean /= Math.Max(1, randomTrials);

            Console.WriteLine($"  {sym,-12}{cls,-9}{both.Equity,10:0.00}x{longOnly.Equity,10:0.00}x{hold,9:0.00}x"
                            + $"{randMean,9:0.00}x{both.Trades,8}{both.ExposurePct * 100,7:0}%{both.MaxDd * 100,7:0}%");

            rows.Add((cls, both.Equity, longOnly.Equity, hold, randMean));
        }

        Console.WriteLine();
        foreach (var cls in rows.Select(r => r.Cls).Distinct().OrderBy(x => x))
        {
            var g = rows.Where(r => r.Cls == cls).ToList();
            Console.WriteLine($"  ── {cls.ToUpperInvariant()} ({g.Count} instruments), median multiple of starting capital");
            Console.WriteLine($"     ladder {Median(g.Select(x => x.Lad)):0.00}x · long-only {Median(g.Select(x => x.Long)):0.00}x · "
                            + $"hold {Median(g.Select(x => x.Hold)):0.00}x · random params {Median(g.Select(x => x.Rand)):0.00}x");
            Console.WriteLine($"     ladder beats hold on {g.Count(x => x.Lad > x.Hold)}/{g.Count} · "
                            + $"beats its own random-parameter arm on {g.Count(x => x.Lad > x.Rand)}/{g.Count} · "
                            + $"the short side helps on {g.Count(x => x.Lad > x.Long)}/{g.Count}");
            Console.WriteLine();
        }

        Console.WriteLine("Reading it: beating hold is the least of it — a long-biased rule in a rising asset does");
        Console.WriteLine("that by accident. The columns that carry information are 'random params' (do the chosen");
        Console.WriteLine("numbers matter?) and 'long-only' (does being always-in actually pay?).");

        Sweep(files, clickAtr);
        return 0;
    }

    /// <summary>
    /// Two follow-up questions the headline table cannot answer. Is the ladder losing because the
    /// RULE is wrong, or because it trades five hundred times and pays for every one? And does
    /// widening the rungs — the obvious fix for churn — rescue it?
    /// </summary>
    private static void Sweep(List<string> files, double baseClick)
    {
        Console.WriteLine();
        Console.WriteLine("── Is it the rule or the churn? Median multiple by click size, with and without costs");
        Console.WriteLine($"   {"click",-8}{"crypto cost",13}{"crypto free",13}{"equity cost",13}{"equity free",13}{"med trades",12}");

        foreach (double click in new[] { 0.5, 1.0, 2.0, 4.0 })
        {
            var cc = new List<double>(); var cf = new List<double>();
            var ec = new List<double>(); var ef = new List<double>(); var tr = new List<double>();

            foreach (var f in files)
            {
                var snap = SnapshotCommand.Load(f);
                if (snap.Bars.Count < 600) continue;
                var bars = snap.Bars.ToArray();
                var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars, 14);
                bool crypto = Path.GetFileName(f).StartsWith("bitstamp") || Path.GetFileName(f).StartsWith("mexc");

                var withCost = Simulate(bars, atr, 3, 2, click, true);
                var noCost = Simulate(bars, atr, 3, 2, click, true, costPerSide: 0.0);
                (crypto ? cc : ec).Add(withCost.Equity);
                (crypto ? cf : ef).Add(noCost.Equity);
                tr.Add(withCost.Trades);
            }

            Console.WriteLine($"   {click + " ATR",-8}{Median(cc),12:0.00}x{Median(cf),12:0.00}x"
                            + $"{Median(ec),12:0.00}x{Median(ef),12:0.00}x{Median(tr),12:0}");
        }
        Console.WriteLine();
        Console.WriteLine("   If the free column is healthy and the cost column is not, the idea is sound and the");
        Console.WriteLine("   frequency is wrong. If both are poor, widening the rungs will not save it.");
    }

    /// <summary>
    /// One pass of the ladder. Costs are charged on every entry and exit — a system that trades
    /// constantly is exactly the kind whose edge lives or dies on them.
    /// </summary>
    private static Book Simulate(Ohlcv[] bars, double[] atr, int clicks, int trail, double clickAtr, bool allowShort, double costPerSide = 0.0010)
    {
        double CostPerSide = costPerSide;

        double equity = 1.0, peak = 1.0, maxDd = 0;
        double reference = bars[200].Close;
        int position = 0;                        // -1, 0, +1
        double entryPx = 0, best = 0;
        int trades = 0, wins = 0, barsIn = 0;

        for (int i = 201; i < bars.Length; i++)
        {
            double a = atr[i];
            if (double.IsNaN(a) || a <= 0) continue;
            double click = clickAtr * a;
            double px = bars[i].Close;

            if (position != 0)
            {
                barsIn++;
                // Mark to market on the bar's move.
                double ret = (px - bars[i - 1].Close) / bars[i - 1].Close * position;
                equity *= 1 + ret;
                peak = Math.Max(peak, equity);
                maxDd = Math.Max(maxDd, (peak - equity) / peak);

                best = position > 0 ? Math.Max(best, px) : Math.Min(best, px);
                bool stopped = position > 0 ? px <= best - trail * click : px >= best + trail * click;
                if (stopped)
                {
                    equity *= 1 - CostPerSide;
                    if ((px - entryPx) * position > 0) wins++;
                    position = 0;
                    reference = px;              // the ladder restarts from where it was stopped
                }
                continue;
            }

            // Flat: count clicks from the reference.
            double moved = (px - reference) / click;
            if (moved >= clicks) { position = 1; }
            else if (moved <= -clicks && allowShort) { position = -1; }
            else if (moved <= -clicks) { reference = px; continue; }   // long-only: re-anchor, stay flat
            else continue;

            entryPx = px; best = px; trades++;
            equity *= 1 - CostPerSide;
        }

        return new Book(equity, maxDd, trades, trades == 0 ? 0 : wins / (double)trades,
                        barsIn / (double)Math.Max(1, bars.Length - 201));
    }

    private static double Median(IEnumerable<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    private static int StableSeed(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }
}
