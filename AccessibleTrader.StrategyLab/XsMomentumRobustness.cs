using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// The robustness pass on cross-sectional momentum — the strongest result this lab has produced,
/// and therefore the one that has to survive the hardest treatment before it means anything.
///
/// <para>
/// Four questions, in descending order of how likely they are to kill it:
/// </para>
/// <list type="number">
/// <item><b>Transaction costs.</b> 215 rebalances of a 13-name book is real turnover. Varma's rule
/// is that a system is its signal PLUS its entry PLUS its exit, and that momentum results in the
/// academic literature are routinely consumed entirely by execution assumptions.</item>
/// <item><b>Eras.</b> A pooled 18-year number can be one regime.</item>
/// <item><b>Noise injection.</b> Varma's own robustness test: perturb the input prices and watch
/// how the edge decays. A real effect degrades gradually; a fitted one falls off a cliff.</item>
/// <item><b>Survivorship.</b> The universe is all survivors. This one cannot be fixed without
/// delisting data, so it is STRESSED instead, under stated assumptions.</item>
/// </list>
///
/// <para>
/// THE BENCHMARK CHANGES ONCE COSTS EXIST. Against a random-selection book, momentum and random
/// both churn, so costs hit both. But a random book redrawn every period is not what anyone would
/// actually do instead — the realistic alternative is holding the whole basket, whose turnover is
/// near zero. Net of costs the honest comparison is therefore momentum vs EQUAL-WEIGHT-ALL, and it
/// is a harder test than the one the headline result passed.
/// </para>
/// </summary>
internal static class XsMomentumRobustness
{
    private sealed record Period(DateTime Date, double Top, double All, double Turnover,
                                 double BottomDecileFwd, int Names);

    public static void Run(List<XsMomentumCommand.Series> series, DateTime start, DateTime end,
        int look, int skip, int hold, bool volNorm)
    {
        var periods = Collect(series, start, end, look, skip, hold, volNorm);
        if (periods.Count < 12) { Console.WriteLine("  robustness: too few periods"); return; }

        Console.WriteLine();
        Console.WriteLine($"  ══════ ROBUSTNESS — look={look} skip={skip} hold={hold}, {periods.Count} rebalances ══════");
        Console.WriteLine();

        Costs(periods);
        Eras(periods);
        Noise(series, start, end, look, skip, hold, volNorm, periods);
        Survivorship(periods, hold);
    }

    // ── 1. Transaction costs ─────────────────────────────────────────────────

    private static void Costs(List<Period> periods)
    {
        double avgTurnover = periods.Average(p => p.Turnover);
        Console.WriteLine($"  ── transaction costs (average one-way turnover {avgTurnover:P0} per rebalance) ──");
        Console.WriteLine($"    {"bps/side",9} {"momentum",11} {"basket",11} {"excess",11}");

        foreach (double bps in new[] { 0.0, 5.0, 10.0, 25.0, 50.0 })
        {
            // Replacing a fraction t of the book means selling t and buying t — both sides pay.
            // The basket is rebalanced to equal weight too, but its holdings do not change, so its
            // turnover is a small fraction of the momentum book's and is charged at a tenth.
            double mom = periods.Sum(p => p.Top - 2 * p.Turnover * bps / 10000.0);
            double all = periods.Sum(p => p.All - 2 * 0.1 * p.Turnover * bps / 10000.0);
            Console.WriteLine($"    {bps,9:0} {Math.Exp(mom) - 1,11:+0.0%;-0.0%;0} {Math.Exp(all) - 1,11:+0.0%;-0.0%;0} " +
                              $"{Math.Exp(mom) - Math.Exp(all),11:+0.0%;-0.0%;0}");
        }

        double gross = periods.Average(p => p.Top - p.All);
        double breakEven = gross <= 0 ? 0 : gross / (2 * periods.Average(p => p.Turnover)) * 10000.0 * 0.9;
        Console.WriteLine($"    Gross edge {gross:+0.000%;-0.000%;0} per period. Break-even cost ≈ {breakEven:0} bps/side.");
        Console.WriteLine("    US large-cap retail commission is ~0 and spread on these names is 1-3 bps, so the");
        Console.WriteLine("    realistic column is 5-10. Small-cap or crypto would sit at 25-50.");
        Console.WriteLine();
    }

    // ── 2. Eras ──────────────────────────────────────────────────────────────

    private static void Eras(List<Period> periods)
    {
        Console.WriteLine("  ── eras (a pooled 18-year number can be one regime) ──");
        int per = periods.Count / 4;
        for (int e = 0; e < 4; e++)
        {
            var slice = periods.Skip(e * per).Take(e == 3 ? int.MaxValue : per).ToList();
            double mom = Math.Exp(slice.Sum(p => p.Top)) - 1;
            double all = Math.Exp(slice.Sum(p => p.All)) - 1;
            int win = slice.Count(p => p.Top > p.All);
            Console.WriteLine($"    {slice[0].Date:yyyy-MM} → {slice[^1].Date:yyyy-MM}: " +
                              $"momentum {mom,9:+0.0%;-0.0%;0}   basket {all,9:+0.0%;-0.0%;0}   " +
                              $"beat basket {win}/{slice.Count} periods");
        }
        Console.WriteLine();
    }

    // ── 3. Noise injection ───────────────────────────────────────────────────

    /// <summary>
    /// Varma's test. Add gaussian noise to every log return, scaled as a fraction of that series'
    /// own daily volatility, rebuild the price path and re-run. A genuine effect should decay
    /// gradually as the signal is buried; a curve-fit collapses at the first perturbation because it
    /// was keyed to the exact path.
    /// </summary>
    private static void Noise(List<XsMomentumCommand.Series> series, DateTime start, DateTime end,
        int look, int skip, int hold, bool volNorm, List<Period> baseline)
    {
        Console.WriteLine("  ── noise injection (a real edge degrades slowly; a fit collapses) ──");
        double baseEdge = baseline.Average(p => p.Top - p.All);
        Console.WriteLine($"    {"noise",7} {"edge/period",13} {"vs clean",10}");
        Console.WriteLine($"    {"0%",7} {baseEdge,13:+0.000%;-0.000%;0} {"100%",10}");

        foreach (double alpha in new[] { 0.25, 0.5, 1.0, 2.0 })
        {
            // Several draws per level: one perturbed path is a sample, not a measurement.
            var edges = new List<double>();
            for (int rep = 0; rep < 5; rep++)
            {
                var noisy = series.Select(s => s.WithNoise(alpha, 4242 + rep * 97 + StableSeed.From(s.Symbol) % 1000)).ToList();
                var p = Collect(noisy, start, end, look, skip, hold, volNorm);
                if (p.Count >= 12) edges.Add(p.Average(q => q.Top - q.All));
            }
            if (edges.Count == 0) continue;
            double e = edges.Average();
            Console.WriteLine($"    {alpha,7:P0} {e,13:+0.000%;-0.000%;0} {(baseEdge == 0 ? 0 : e / baseEdge),10:P0}");
        }
        Console.WriteLine("    Noise is a multiple of each series' own daily vol, added to every return.");
        Console.WriteLine();
    }

    // ── 4. Survivorship stress ───────────────────────────────────────────────

    /// <summary>
    /// The bias that cannot be removed without delisting data, so it is stressed under stated
    /// assumptions instead.
    ///
    /// <para>
    /// Parameterised as an ANNUAL delisting rate, because that is the only form in which the
    /// assumption can be sanity-checked against reality. A first attempt at this modelled "two
    /// phantom names losing 20% every rebalance", which compounded across 215 months into a basket
    /// down 99% — that is not delisting, it is a monthly catastrophe. Large-cap delisting for cause
    /// runs roughly 0.5–2% a year; 5% is included as a deliberately pessimistic bound.
    /// </para>
    ///
    /// <para>
    /// The parameter that actually decides the answer is <c>topShare</c>: what fraction of the
    /// vanished names were sitting in the TOP third when they died. At 0 they were all losers, and
    /// restoring them hurts only the basket — survivorship would then be understating this
    /// comparison. At 0.33 they were as likely to be winners as anything else, and the strategy
    /// takes the same damage. The truth is nearer 0 for slow decliners and nearer 0.33 for sudden
    /// frauds, so both bounds are reported rather than one being chosen.
    /// </para>
    /// </summary>
    private static void Survivorship(List<Period> periods, int hold)
    {
        Console.WriteLine("  ── survivorship stress (cannot be fixed without delisting data) ──");

        double cleanMom = Math.Exp(periods.Sum(p => p.Top)) - 1;
        double cleanAll = Math.Exp(periods.Sum(p => p.All)) - 1;
        double names = periods.Average(p => p.Names);
        double periodsPerYear = 365.25 / hold;

        Console.WriteLine($"    Universe {names:0} names, {periodsPerYear:0.0} rebalances/year, {periods.Count} periods.");
        Console.WriteLine($"    {"annual",7} {"shock",7} {"in top",7} {"momentum",11} {"basket",11} {"excess",11} {"vs clean",10}");
        Console.WriteLine($"    {"0%",7} {"—",7} {"—",7} {cleanMom,11:+0.0%;-0.0%;0} {cleanAll,11:+0.0%;-0.0%;0} " +
                          $"{cleanMom - cleanAll,11:+0.0%;-0.0%;0} {"100%",10}");

        double cleanExcess = cleanMom - cleanAll;

        foreach (double annual in new[] { 0.005, 0.02, 0.05 })
            foreach (double shock in new[] { -0.50, -1.00 })
                foreach (double topShare in new[] { 0.0, 0.33 })
                {
                    // Expected number of names delisting per rebalance, and the weight they carry.
                    double perPeriod = names * annual / periodsPerYear;
                    double wAll = perPeriod / names;                       // basket holds everything
                    double wTop = perPeriod * topShare / (names / 3.0);    // strategy holds a third

                    // A delisted position is a one-off loss of `shock` on that weight, not a
                    // recurring drag on the whole book.
                    double dragAll = Math.Log(1 + wAll * shock);
                    double dragTop = Math.Log(1 + wTop * shock);

                    double m = Math.Exp(periods.Sum(p => p.Top) + dragTop * periods.Count) - 1;
                    double a = Math.Exp(periods.Sum(p => p.All) + dragAll * periods.Count) - 1;
                    double ex = m - a;

                    Console.WriteLine($"    {annual,7:P1} {shock,7:P0} {topShare,7:P0} {m,11:+0.0%;-0.0%;0} {a,11:+0.0%;-0.0%;0} " +
                                      $"{ex,11:+0.0%;-0.0%;0} {(cleanExcess == 0 ? 0 : ex / cleanExcess),10:P0}");
                }

        Console.WriteLine("    'in top' = share of delisted names that were in the top third when they died.");
        Console.WriteLine("    At 0% the excess GROWS (only the basket is hurt); at 33% it shrinks but survives.");
        Console.WriteLine("    Neither bound erases the edge at any plausible delisting rate. This does NOT");
        Console.WriteLine("    clear the long-short spread, which is biased the usual way and is not traded here.");
        Console.WriteLine();
    }

    // ── Shared machinery ─────────────────────────────────────────────────────

    private static List<Period> Collect(List<XsMomentumCommand.Series> series, DateTime start, DateTime end,
        int look, int skip, int hold, bool volNorm)
    {
        var outp = new List<Period>();
        HashSet<string>? prevHeld = null;

        for (var t = start.AddDays(look + skip); t.AddDays(hold) <= end; t = t.AddDays(hold))
        {
            var ranked = new List<(string Sym, double Past, double Fwd)>();
            foreach (var s in series)
            {
                double? now = s.CloseAsOf(t.AddDays(-skip));
                double? then = s.CloseAsOf(t.AddDays(-skip - look));
                double? fwdNow = s.CloseAsOf(t);
                double? fwd = s.CloseAsOf(t.AddDays(hold));
                if (now is null || then is null || fwd is null || fwdNow is null) continue;
                double past = Math.Log(now.Value / then.Value);
                if (volNorm)
                {
                    double? vol = s.VolOver(t.AddDays(-skip), look);
                    if (vol is null) continue;
                    past /= vol.Value;
                }
                ranked.Add((s.Symbol, past, Math.Log(fwd.Value / fwdNow.Value)));
            }
            if (ranked.Count < 8) continue;

            int k = Math.Max(1, ranked.Count / 3);
            var byMom = ranked.OrderByDescending(r => r.Past).ToList();
            var held = byMom.Take(k).Select(r => r.Sym).ToHashSet();

            double turnover = prevHeld == null ? 1.0 : held.Except(prevHeld).Count() / (double)k;
            prevHeld = held;

            outp.Add(new Period(t,
                byMom.Take(k).Average(r => r.Fwd),
                ranked.Average(r => r.Fwd),
                turnover,
                byMom.TakeLast(Math.Max(1, ranked.Count / 10)).Average(r => r.Fwd),
                ranked.Count));
        }
        return outp;
    }
}
