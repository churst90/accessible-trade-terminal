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
        // Survivorship now RE-RANKS a truncated universe, so it needs the same inputs the
        // clean run had — a haircut needed only the results, which is exactly what made it
        // incapable of failing.
        Survivorship(periods, series, start, end, look, skip, hold, volNorm);
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
    private static void Survivorship(List<Period> periods,
        List<XsMomentumCommand.Series> series, DateTime start, DateTime end,
        int look, int skip, int hold, bool volNorm)
    {
        Console.WriteLine("  ── survivorship stress (cannot be fixed without delisting data) ──");

        double cleanMom = Math.Exp(periods.Sum(p => p.Top)) - 1;
        double cleanAll = Math.Exp(periods.Sum(p => p.All)) - 1;
        double names = periods.Average(p => p.Names);
        double periodsPerYear = 365.25 / hold;

        Console.WriteLine($"    Universe {names:0} names, {periodsPerYear:0.0} rebalances/year, {periods.Count} periods.");
        Console.WriteLine($"    {"annual",7} {"shock",7} {"frm bot",7} {"momentum",11} {"basket",11} {"excess",11} {"vs clean",10}");
        Console.WriteLine($"    {"0%",7} {"—",7} {"—",7} {cleanMom,11:+0.0%;-0.0%;0} {cleanAll,11:+0.0%;-0.0%;0} " +
                          $"{cleanMom - cleanAll,11:+0.0%;-0.0%;0} {"100%",10}");

        double cleanExcess = cleanMom - cleanAll;

        // ── The stress RE-RANKS. It used to be a uniform haircut. ─────────────
        //
        // The old version computed two constants — dragTop and dragAll — that did not depend
        // on the ranking at all, and added them uniformly to every period's log return. At the
        // headline topShare of 0.33, wTop = perPeriod * 0.33 / (names/3) equals wAll by
        // construction, so dragTop == dragAll and the whole table collapsed to
        // `excess = e^(drag*N) * cleanExcess` — the clean excess rescaled by a POSITIVE
        // CONSTANT. **It could never change sign.** Verify against the published table in
        // docs/XSMOMENTUM_FINDINGS.md: at 5%/33% the drag works out to e^-0.875 = 0.417, and
        // the doc's "vs clean" column reads 42%. At topShare = 0 the drag on the momentum book
        // is exactly zero, so the excess mechanically GROWS — which the doc then reported as a
        // finding. "The edge survives every cell" was arithmetic, not evidence.
        //
        // Survivorship bias in a RANKING study is a TRUNCATION OF THE CROSS-SECTION, not a
        // haircut on the book: the names that vanish are gone from the universe you rank, so
        // they cannot be selected and the ranking itself changes. Modelled as a haircut it
        // cannot touch the ranking, which is precisely why the old table was incapable of
        // failing. This is the same criticism the lab correctly levels at the Trading Cross
        // video's Monte Carlo.
        //
        // So: at each rebalance, remove `annual/periodsPerYear` of the names, drawn
        // PREFERENTIALLY from the bottom of the trailing-return ranking (that is where
        // delistings actually come from), apply `shock` to the removed names' forward returns
        // for the basket that held them, and re-rank the survivors.
        foreach (double annual in new[] { 0.005, 0.02, 0.05 })
            foreach (double shock in new[] { -0.50, -1.00 })
                foreach (double bottomBias in new[] { 1.0, 0.5 })
                {
                    var stressed = CollectWithDelistings(
                        series, start, end, look, skip, hold, volNorm,
                        annualDelistRate: annual, shock: shock, bottomBias: bottomBias,
                        seed: 20260827);

                    if (stressed.Count == 0) continue;

                    double m = Math.Exp(stressed.Sum(p => p.Top)) - 1;
                    double a = Math.Exp(stressed.Sum(p => p.All)) - 1;
                    double ex = m - a;

                    Console.WriteLine($"    {annual,7:P1} {shock,7:P0} {bottomBias,7:P0} {m,11:+0.0%;-0.0%;0} {a,11:+0.0%;-0.0%;0} " +
                                      $"{ex,11:+0.0%;-0.0%;0} {(cleanExcess == 0 ? 0 : ex / cleanExcess),10:P0}");
                }

        Console.WriteLine("    'from bottom' = share of delistings drawn from the bottom half of the");
        Console.WriteLine("    trailing-return ranking; 100% is the harshest realistic assumption and 50%");
        Console.WriteLine("    is delisting-at-random. Removed names are gone from the RANKING, not merely");
        Console.WriteLine("    charged a fee — so this stress CAN change the sign, which the previous");
        Console.WriteLine("    uniform-haircut version could not. This does NOT clear the long-short");
        Console.WriteLine("    spread, which is biased the usual way and is not traded here.");
        Console.WriteLine();
    }

    // ── Shared machinery ─────────────────────────────────────────────────────


    /// <summary>
    /// <see cref="Collect"/> with names DELISTING out of the universe as it runs.
    ///
    /// <para>At each rebalance a share of the surviving names is removed, drawn preferentially
    /// from the bottom of the trailing-return ranking — which is where delistings actually come
    /// from. A removed name takes <paramref name="shock"/> on its final forward return for the
    /// basket that held it, and is then <b>gone from the ranking</b>, so the momentum book is
    /// selected from a smaller cross-section for the rest of the run.</para>
    ///
    /// <para>That last part is the whole difference from the uniform haircut this replaced: a
    /// haircut cannot touch the ranking, so it can only rescale the answer. Truncation can
    /// change which names are held, and therefore can change the sign.</para>
    /// </summary>
    private static List<Period> CollectWithDelistings(
        List<XsMomentumCommand.Series> series, DateTime start, DateTime end,
        int look, int skip, int hold, bool volNorm,
        double annualDelistRate, double shock, double bottomBias, int seed)
    {
        var outp = new List<Period>();
        var alive = series.Select(s => s.Symbol).ToHashSet(StringComparer.Ordinal);
        var rng = new Random(seed);
        HashSet<string>? prevHeld = null;

        double periodsPerYear = 365.0 / Math.Max(1, hold);

        for (var t = start.AddDays(look + skip); t.AddDays(hold) <= end; t = t.AddDays(hold))
        {
            var ranked = new List<(string Sym, double Past, double Fwd)>();
            foreach (var s in series)
            {
                if (!alive.Contains(s.Symbol)) continue;   // already delisted — not rankable

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

            var byMom = ranked.OrderByDescending(r => r.Past).ToList();

            // Who dies this period. Expected count from the annual rate; each draw comes from
            // the bottom half with probability `bottomBias`, from anywhere otherwise.
            //
            // ROUNDED, this was zero in every row of every table. 39 names at 5%/yr over 12.2
            // rebalances a year is 0.16 expected deaths per period, and Math.Round takes that to
            // 0 — so the "stress" that replaced the algebraically-unfailable haircut was itself
            // unfailable, for a different reason, and printed "100% vs clean" thirteen times
            // without anyone reading it as a null result. Stochastic rounding makes the expected
            // count come out right over the run: 0.16 per period is a death about one period in
            // six, ~34 of them across 215 periods, which is what 5% a year for 17.6 years means.
            double expectedDeaths = byMom.Count * annualDelistRate / periodsPerYear;
            int dying = (int)Math.Floor(expectedDeaths);
            if (rng.NextDouble() < expectedDeaths - Math.Floor(expectedDeaths)) dying++;
            var doomed = new HashSet<string>(StringComparer.Ordinal);
            int half = Math.Max(1, byMom.Count / 2);
            for (int i = 0; i < dying && doomed.Count < byMom.Count; i++)
            {
                bool fromBottom = rng.NextDouble() < bottomBias;
                int idx = fromBottom
                    ? byMom.Count - 1 - rng.Next(half)
                    : rng.Next(byMom.Count);
                doomed.Add(byMom[idx].Sym);
            }

            // A doomed name's forward return for THIS period is the shock.
            double FwdOf((string Sym, double Past, double Fwd) r) =>
                doomed.Contains(r.Sym) ? Math.Log(Math.Max(1e-6, 1 + shock)) : r.Fwd;

            int k = Math.Max(1, byMom.Count / 3);
            var held = byMom.Take(k).Select(r => r.Sym).ToHashSet(StringComparer.Ordinal);
            double turnover = prevHeld == null ? 1.0 : held.Except(prevHeld).Count() / (double)k;
            prevHeld = held;

            outp.Add(new Period(t,
                byMom.Take(k).Average(FwdOf),
                byMom.Average(FwdOf),
                turnover,
                byMom.TakeLast(Math.Max(1, byMom.Count / 10)).Average(FwdOf),
                byMom.Count));

            // They are gone from here on — this is the truncation the haircut could not model.
            foreach (var sym in doomed) alive.Remove(sym);
        }

        return outp;
    }

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
