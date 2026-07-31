namespace AccessibleTrader.StrategyLab;

/// <summary>
/// The robustness pass on the SMA(200) dip filter — the lab's other surviving result, and the one
/// most likely to die here.
///
/// <para>
/// WHY IT IS AT RISK IN A WAY CROSS-SECTIONAL MOMENTUM WAS NOT. The dip filter's edge is
/// <b>+0.10R per trade</b> over a random-entry baseline. Cross-sectional momentum turns over 17% of
/// a book once a month; this fires a fresh round trip every single time. And because risk is one
/// ATR, the position is <c>1 / (ATR÷price)</c> times the risk budget — for an equity with a 2% ATR
/// that is 50× leverage on the risk unit, so a spread of a few basis points against NOTIONAL
/// becomes a large number against R. A 10 bps round trip at a 2% ATR costs 0.10R, which is the
/// entire edge.
/// </para>
///
/// <para>
/// That arithmetic is the reason per-trade R-multiple results need costs expressed in R before they
/// mean anything, and it is why this test comes before any other.
/// </para>
/// </summary>
internal static class GateRobustness
{
    /// <summary>The signals whose lift is the actual claim: mean-reversion entries. Breakout and
    /// short arms showed nothing, and the random arm IS the control.</summary>
    private static readonly string[] Claimed = { "cipherB-long", "rsi-bounce-long", "z-reversion-long*" };

    public static void Run(
        List<(string Signal, double R, bool MaGate, string Symbol, string Class, DateTime Date, double AtrPct)> all,
        int permutations)
    {
        // Equities only. The result was an equity result — crypto's arm was n=184 and never cleared.
        var eq = all.Where(t => t.Class == "equity").ToList();
        if (eq.Count < 500) { Console.WriteLine("  gate robustness: too few equity trades"); return; }

        Console.WriteLine();
        Console.WriteLine($"  ══════ ROBUSTNESS — SMA(200) dip filter, {eq.Count:N0} equity trades ══════");
        Console.WriteLine();

        Costs(eq);
        Eras(eq, permutations);
        PerSymbol(eq);
        Survivorship(eq);
    }

    // ── 1. Costs, in R ───────────────────────────────────────────────────────

    private static void Costs(List<(string Signal, double R, bool MaGate, string Symbol, string Class, DateTime Date, double AtrPct)> eq)
    {
        double medAtr = eq.Select(t => t.AtrPct).OrderBy(v => v).ElementAt(eq.Count / 2);
        Console.WriteLine($"  ── transaction costs (median ATR = {medAtr:P2} of price, so 1R ≈ {medAtr:P2} of notional) ──");
        Console.WriteLine("    Cost per round trip in R = 2 × bps ÷ 10000 ÷ atrPct, charged per trade.");
        Console.WriteLine($"    {"bps/side",9} {"gated mean",11} {"random gated",13} {"EXCESS",10}");

        var rnd = eq.Where(t => t.Signal == "random-entry-long").ToList();

        foreach (double bps in new[] { 0.0, 2.0, 5.0, 10.0 })
        {
            double CostR(double atrPct) => atrPct <= 0 ? 0 : 2 * bps / 10000.0 / atrPct;

            foreach (var sig in Claimed)
            {
                var set = eq.Where(t => t.Signal == sig).ToList();
                if (set.Count < 50) continue;
                var open = set.Where(t => t.MaGate).ToList();
                if (open.Count < 30) continue;

                double gatedNet = open.Average(t => t.R - CostR(t.AtrPct));
                double baseNet = set.Average(t => t.R - CostR(t.AtrPct));
                double lift = gatedNet - baseNet;

                var rOpen = rnd.Where(t => t.MaGate).ToList();
                double rndLift = rOpen.Count >= 30
                    ? rOpen.Average(t => t.R - CostR(t.AtrPct)) - rnd.Average(t => t.R - CostR(t.AtrPct))
                    : 0;

                if (sig == Claimed[0])
                    Console.WriteLine($"    {bps,9:0} {"",11} {"",13} {"",10}");
                Console.WriteLine($"      {sig,-20} gated {gatedNet,+7:+0.000;-0.000;0}R   lift {lift,+7:+0.000;-0.000;0}R   " +
                                  $"excess over random {lift - rndLift,+7:+0.000;-0.000;0}R");
            }
        }

        Console.WriteLine();
        Console.WriteLine("    NOTE the lift is a RELATIVE quantity — costs hit gated and ungated trades alike, so");
        Console.WriteLine("    they largely cancel out of it. What costs actually destroy is the ABSOLUTE return:");
        var cb = eq.Where(t => t.Signal == "cipherB-long" && t.MaGate).ToList();
        foreach (double bps in new[] { 0.0, 2.0, 5.0, 10.0 })
        {
            double net = cb.Average(t => t.R - (t.AtrPct <= 0 ? 0 : 2 * bps / 10000.0 / t.AtrPct));
            Console.WriteLine($"      cipherB-long gated, {bps,4:0} bps/side: {net,+7:+0.000;-0.000;0}R per trade" +
                              (net <= 0 ? "   ← no longer profitable" : ""));
        }
        Console.WriteLine();
    }

    // ── 2. Eras ──────────────────────────────────────────────────────────────

    private static void Eras(List<(string Signal, double R, bool MaGate, string Symbol, string Class, DateTime Date, double AtrPct)> eq,
        int permutations)
    {
        Console.WriteLine("  ── eras ──");
        foreach (var sig in Claimed.Concat(new[] { "random-entry-long" }))
        {
            var set = eq.Where(t => t.Signal == sig).OrderBy(t => t.Date).ToList();
            if (set.Count < 200) continue;
            Console.Write($"    {sig,-20}");
            int per = set.Count / 4;
            for (int e = 0; e < 4; e++)
            {
                var slice = set.Skip(e * per).Take(e == 3 ? int.MaxValue : per).ToList();
                var open = slice.Where(t => t.MaGate).ToList();
                var shut = slice.Where(t => !t.MaGate).ToList();
                if (open.Count < 15 || shut.Count < 15) { Console.Write("     n/a"); continue; }
                Console.Write($" {open.Average(t => t.R) - shut.Average(t => t.R),+8:+0.00;-0.00;0}");
            }
            Console.WriteLine($"   ({set[0].Date:yyyy}→{set[^1].Date:yyyy}, gap per era)");
        }
        Console.WriteLine("    Four equal-count era slices. The random arm should stay near zero in all four —");
        Console.WriteLine("    if it does not, the filter is picking up market direction rather than signal quality.");
        Console.WriteLine();
    }

    // ── 3. Per-symbol ────────────────────────────────────────────────────────

    private static void PerSymbol(List<(string Signal, double R, bool MaGate, string Symbol, string Class, DateTime Date, double AtrPct)> eq)
    {
        Console.WriteLine("  ── per symbol (is the pooled gap a handful of names?) ──");
        foreach (var sig in Claimed)
        {
            var set = eq.Where(t => t.Signal == sig).ToList();
            var gaps = new List<double>();
            foreach (var g in set.GroupBy(t => t.Symbol))
            {
                var open = g.Where(t => t.MaGate).ToList();
                var shut = g.Where(t => !t.MaGate).ToList();
                if (open.Count < 8 || shut.Count < 8) continue;
                gaps.Add(open.Average(t => t.R) - shut.Average(t => t.R));
            }
            if (gaps.Count < 8) { Console.WriteLine($"    {sig,-20} too few symbols"); continue; }
            Console.WriteLine($"    {sig,-20} positive on {gaps.Count(v => v > 0)}/{gaps.Count} symbols   " +
                              $"median gap {gaps.OrderBy(v => v).ElementAt(gaps.Count / 2),+6:+0.00;-0.00;0}R   " +
                              $"mean {gaps.Average(),+6:+0.00;-0.00;0}R");
        }
        Console.WriteLine();
    }

    // ── 4. Survivorship ──────────────────────────────────────────────────────

    /// <summary>
    /// For a DIP-BUYING study the survivorship bias runs the dangerous way, unlike the
    /// cross-sectional case. Every name in this universe recovered from every dip it ever had,
    /// because it is still listed. The names that dipped and kept going to zero are absent, and
    /// those are exactly the trades a dip-buyer would have lost on.
    ///
    /// <para>
    /// Worse for the specific claim: a name heading for delisting spends its final years BELOW its
    /// 200-day average, so the missing losses would land disproportionately in the gate-CLOSED
    /// bucket — which would widen the measured gap rather than narrow it. That is the direction
    /// that flatters the result, so it is the one that has to be stated plainly.
    /// </para>
    /// </summary>
    private static void Survivorship(List<(string Signal, double R, bool MaGate, string Symbol, string Class, DateTime Date, double AtrPct)> eq)
    {
        Console.WriteLine("  ── survivorship ──");
        var cb = eq.Where(t => t.Signal == "cipherB-long").ToList();
        var open = cb.Where(t => t.MaGate).ToList();
        var shut = cb.Where(t => !t.MaGate).ToList();
        if (open.Count < 30 || shut.Count < 30) { Console.WriteLine("    too few"); return; }

        double gap = open.Average(t => t.R) - shut.Average(t => t.R);
        Console.WriteLine($"    measured gap {gap:+0.000;-0.000;0}R on {cb.Count:N0} trades across {cb.Select(t => t.Symbol).Distinct().Count()} surviving names.");
        Console.WriteLine();
        Console.WriteLine("    This bias runs the WRONG way here, unlike the cross-sectional case. Every name in");
        Console.WriteLine("    this universe recovered from every dip it ever had — that is what still being listed");
        Console.WriteLine("    means. The dips that did not recover belong to companies that are gone.");
        Console.WriteLine();
        Console.WriteLine("    And a company heading for delisting spends its last years BELOW its 200-day average,");
        Console.WriteLine("    so those missing losses would land mostly in the gate-CLOSED bucket and would widen");
        Console.WriteLine("    the gap, not narrow it. The filter would look better, for a reason that is an artefact.");
        Console.WriteLine();
        Console.WriteLine("    Unquantifiable without delisting data. Stated rather than stressed, because inventing");
        Console.WriteLine("    a number here would be inventing the answer.");
        Console.WriteLine();
    }
}
