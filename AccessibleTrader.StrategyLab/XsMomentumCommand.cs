namespace AccessibleTrader.StrategyLab;

/// <summary>
/// CROSS-SECTIONAL momentum — rank a basket by trailing return, hold the winners, and ask whether
/// that beats holding the basket.
///
/// <para>
/// WHY THIS MATTERS MORE THAN IT LOOKS. Every study in this lab to date has been TIME-SERIES: one
/// asset measured against its own past. Cross-sectional momentum is a different alpha family
/// entirely — the asset is measured against its PEERS — and it is the most replicated anomaly in
/// academic finance, surviving thirty years of out-of-sample tests across equities, futures,
/// currencies and countries. Samir Varma names it as one of three distinct momentum types and notes
/// it works "even with unrelated assets thrown in the basket". Not having tested it was the biggest
/// gap in this lab's coverage.
/// </para>
///
/// <para>
/// THE CONTROL THAT DECIDES IT. "The top-ranked third went up" is nearly guaranteed in a universe
/// that rose over the sample, and says nothing about ranking. Every result is therefore reported
/// against a RANDOM-SELECTION portfolio — the same number of names, drawn at random at each
/// rebalance from exactly the same eligible set. If ranking by past return does not beat drawing
/// names from a hat, there is no cross-sectional effect, only a rising market.
/// </para>
///
/// <para>
/// SURVIVORSHIP. The snapshot universe is entirely survivors — every symbol still trades today.
/// Cross-sectional momentum is the study most damaged by that, because the names that would have
/// ranked worst are precisely the ones that stopped existing. Treat the long-short spread as an
/// upper bound. The random-selection control absorbs part of this (it draws from the same biased
/// universe) which is a second reason it, and not the raw return, is the number to read.
/// </para>
///
/// <para>
/// Lookbacks and holds are in CALENDAR DAYS rather than bars, so crypto's 365-day year and the
/// equity 252-day year are handled without a class-dependent constant and without bar-index
/// misalignment across instruments with different histories.
/// </para>
/// </summary>
public static class XsMomentumCommand
{
    /// <summary>A price older than this is treated as missing rather than carried forward. Without
    /// it a symbol that stops reporting keeps a stale rank forever.</summary>
    private const int StaleDays = 10;

    /// <summary>Minimum eligible names needed to form a meaningful ranking on a date.</summary>
    private const int MinNames = 8;

    /// <summary>
    /// Random books averaged per configuration. A SINGLE random draw is a sample from a very wide
    /// distribution, not a baseline — over eighteen years one lucky draw of a third of the universe
    /// can compound to several times another. Averaging many draws gives the expected return of
    /// choosing names by coin, which is the quantity the momentum book has to beat.
    /// </summary>
    private const int RandomBooks = 400;

    internal sealed class Series
    {
        public required string Symbol { get; init; }
        public required string Class { get; init; }
        public required DateTime[] Dates { get; init; }
        public required double[] Closes { get; init; }

        /// <summary>
        /// Stdev of daily log returns over the <paramref name="days"/> calendar days ending at
        /// <paramref name="d"/>. Used to rank on RISK-ADJUSTED trailing return.
        ///
        /// <para>
        /// Ranking raw returns across a mixed universe is close to ranking by volatility: a crypto
        /// name will out-return an equity in most windows simply because it moves more, so the top
        /// third fills with the highest-volatility names regardless of any momentum. Dividing by
        /// realised vol is the standard correction and is what makes a mixed basket a fair test
        /// rather than a volatility sort wearing a momentum label.
        /// </para>
        /// </summary>
        public double? VolOver(DateTime d, int days)
        {
            int hi = Index(d), lo = Index(d.AddDays(-days));
            if (hi < 0 || lo < 0 || hi - lo < 20) return null;
            double sum = 0, sumSq = 0; int n = 0;
            for (int i = lo + 1; i <= hi; i++)
            {
                if (Closes[i] <= 0 || Closes[i - 1] <= 0) continue;
                double r = Math.Log(Closes[i] / Closes[i - 1]);
                sum += r; sumSq += r * r; n++;
            }
            if (n < 20) return null;
            double mean = sum / n;
            double v = sumSq / n - mean * mean;
            return v > 1e-12 ? Math.Sqrt(v) : null;
        }

        /// <summary>
        /// A copy of this series with gaussian noise added to every log return, scaled to
        /// <paramref name="alpha"/> times its own daily volatility, and the price path rebuilt.
        /// Varma's robustness test: a genuine edge should decay gradually under this, a curve-fit
        /// keyed to the exact path should collapse.
        /// </summary>
        public Series WithNoise(double alpha, int seed)
        {
            var rets = new double[Closes.Length];
            double sum = 0, sumSq = 0; int n = 0;
            for (int i = 1; i < Closes.Length; i++)
            {
                if (Closes[i] <= 0 || Closes[i - 1] <= 0) { rets[i] = 0; continue; }
                rets[i] = Math.Log(Closes[i] / Closes[i - 1]);
                sum += rets[i]; sumSq += rets[i] * rets[i]; n++;
            }
            if (n < 2) return this;
            double mean = sum / n;
            double sigma = Math.Sqrt(Math.Max(1e-12, sumSq / n - mean * mean));

            var rng = new Random(seed);
            var noisy = new double[Closes.Length];
            noisy[0] = Closes[0];
            for (int i = 1; i < Closes.Length; i++)
            {
                // Box–Muller, so the perturbation is genuinely gaussian rather than uniform.
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                noisy[i] = Math.Max(1e-9, noisy[i - 1] * Math.Exp(rets[i] + alpha * sigma * g));
            }

            return new Series { Symbol = Symbol, Class = Class, Dates = Dates, Closes = noisy };
        }

        private int Index(DateTime d)
        {
            int lo = 0, hi = Dates.Length - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Dates[mid] <= d) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return best;
        }

        /// <summary>Most recent close at or before <paramref name="d"/>, or null if none / stale.</summary>
        public double? CloseAsOf(DateTime d)
        {
            int lo = 0, hi = Dates.Length - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Dates[mid] <= d) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            if (best < 0) return null;
            if ((d - Dates[best]).TotalDays > StaleDays) return null;
            return Closes[best] > 0 ? Closes[best] : null;
        }
    }

    private sealed record Result(int Lookback, int Skip, int Hold,
        double Top, double Bottom, double All, double Random, int Rebalances, int AvgNames);

    public static int Run(string snapshotDir, string tf, string universe, string rank, int permutations)
    {
        var all = Load(snapshotDir, tf);
        var series = universe.ToLowerInvariant() switch
        {
            "crypto" => all.Where(s => s.Class == "crypto").ToList(),
            "equity" => all.Where(s => s.Class is "equity" or "bond" or "commod").ToList(),
            _ => all,
        };

        if (series.Count < MinNames)
        {
            Console.WriteLine($"Only {series.Count} series in universe '{universe}' — need {MinNames}.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"===== CROSS-SECTIONAL MOMENTUM — universe '{universe}', {series.Count} symbols =====");
        Console.WriteLine($"  {string.Join(", ", series.Select(s => s.Symbol).OrderBy(s => s))}");
        Console.WriteLine();
        bool volNorm = rank.Equals("volnorm", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"  Rank by {(volNorm ? "trailing return / realised vol" : "raw trailing return")}, " +
                          "hold the top third, rebalance each hold period.");
        Console.WriteLine("  Lookback/skip/hold in CALENDAR DAYS. Equal-weighted, no costs.");
        Console.WriteLine("  SURVIVORSHIP: every symbol here still trades. Read the RANDOM column, not the raw return.");
        Console.WriteLine();

        DateTime start = series.Max(s => s.Dates[0]);
        DateTime end = series.Min(s => s.Dates[^1]);
        Console.WriteLine($"  Common window {start:yyyy-MM-dd} → {end:yyyy-MM-dd} " +
                          $"({(end - start).TotalDays / 365.25:0.0} years, limited by the shortest history)");
        Console.WriteLine();

        var results = new List<Result>();
        Console.WriteLine($"  {"look",5} {"skip",5} {"hold",5} {"TOP",9} {"bottom",9} {"all",9} {"RANDOM",9} {"top−rand",9} {"reb",5} {"names",6}");

        foreach (int look in new[] { 30, 90, 180, 365 })
            foreach (int skip in new[] { 0, 30 })
                foreach (int hold in new[] { 30, 90 })
                {
                    var r = Backtest(series, start, end, look, skip, hold, volNorm);
                    if (r == null) continue;
                    results.Add(r);
                    Console.WriteLine($"  {look,5} {skip,5} {hold,5} " +
                                      $"{r.Top,9:+0.0%;-0.0%;0} {r.Bottom,9:+0.0%;-0.0%;0} {r.All,9:+0.0%;-0.0%;0} " +
                                      $"{r.Random,9:+0.0%;-0.0%;0} {r.Top - r.Random,9:+0.0%;-0.0%;0} " +
                                      $"{r.Rebalances,5} {r.AvgNames,6}");
                }

        if (results.Count == 0) { Console.WriteLine("  No usable configurations."); return 1; }

        Report(results, series, start, end, volNorm, permutations);
        return 0;
    }

    /// <summary>
    /// One configuration. Returns TOTAL log return of each book over the whole window, so the
    /// numbers compound and are directly comparable.
    ///
    /// <para>
    /// The random book draws from the SAME eligible set on the SAME dates with the SAME name count.
    /// Anything the market did, it did to both books equally; the only difference is whether the
    /// names were chosen by trailing return or by a coin.
    /// </para>
    /// </summary>
    private static Result? Backtest(List<Series> series, DateTime start, DateTime end,
        int look, int skip, int hold, bool volNorm)
    {
        double top = 0, bottom = 0, all = 0;
        var rndBooks = new double[RandomBooks];
        int rebalances = 0, nameSum = 0;
        var rng = new Random(20260731 + look * 1000 + skip * 100 + hold);

        for (var t = start.AddDays(look + skip); t.AddDays(hold) <= end; t = t.AddDays(hold))
        {
            // Eligible = has a price now, a price at the lookback start, and a price at the horizon.
            var ranked = new List<(Series S, double Past, double Fwd)>();
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
                ranked.Add((s, past, Math.Log(fwd.Value / fwdNow.Value)));
            }

            if (ranked.Count < MinNames) continue;

            int k = Math.Max(1, ranked.Count / 3);
            var byMom = ranked.OrderByDescending(r => r.Past).ToList();

            top += byMom.Take(k).Average(r => r.Fwd);
            bottom += byMom.TakeLast(k).Average(r => r.Fwd);
            all += ranked.Average(r => r.Fwd);

            // Each random book keeps its own running total, so what is compared at the end is the
            // AVERAGE compounded outcome of choosing by coin — not one draw's luck.
            var pool = ranked.ToArray();
            for (int b = 0; b < RandomBooks; b++)
            {
                for (int i = pool.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
                double acc = 0;
                for (int i = 0; i < k; i++) acc += pool[i].Fwd;
                rndBooks[b] += acc / k;
            }

            rebalances++;
            nameSum += ranked.Count;
        }

        if (rebalances < 8) return null;

        return new Result(look, skip, hold,
            Math.Exp(top) - 1, Math.Exp(bottom) - 1, Math.Exp(all) - 1,
            rndBooks.Select(v => Math.Exp(v) - 1).Average(),
            rebalances, nameSum / rebalances);
    }

    /// <summary>
    /// Per-period spread with a permutation test, on the single best-supported configuration.
    ///
    /// <para>
    /// The permutation reshuffles the RANK LABELS within each rebalance date and leaves each name's
    /// realised forward return attached to that name. This holds the market's own behaviour on that
    /// date completely fixed and asks only whether sorting by past return picked better names than
    /// sorting at random — which is the whole claim.
    /// </para>
    /// </summary>

    /// <summary>
    /// The per-period top-minus-bottom spreads for ONE grid configuration, plus the pooled
    /// (past, forward) rows each period was ranked from — the raw material a permutation
    /// shuffles.
    ///
    /// <para>Extracted so the max-statistic null can build this for EVERY cell, not just the
    /// winner. It was inlined for the winner alone, which is what made the post-selection
    /// p-value structurally hard to notice.</para>
    /// </summary>
    private static (List<double> Spreads, List<(double Past, double Fwd)[]> Pooled) PerPeriodSpreads(
        List<Series> series, DateTime start, DateTime end, Result cfg, bool volNorm)
    {
        var spreads = new List<double>();
        var pooled = new List<(double Past, double Fwd)[]>();

        for (var t = start.AddDays(cfg.Lookback + cfg.Skip); t.AddDays(cfg.Hold) <= end; t = t.AddDays(cfg.Hold))
        {
            var ranked = new List<(double Past, double Fwd)>();
            foreach (var s in series)
            {
                double? now = s.CloseAsOf(t.AddDays(-cfg.Skip));
                double? then = s.CloseAsOf(t.AddDays(-cfg.Skip - cfg.Lookback));
                double? fwdNow = s.CloseAsOf(t);
                double? fwd = s.CloseAsOf(t.AddDays(cfg.Hold));
                if (now is null || then is null || fwd is null || fwdNow is null) continue;
                double past = Math.Log(now.Value / then.Value);
                if (volNorm)
                {
                    double? vol = s.VolOver(t.AddDays(-cfg.Skip), cfg.Lookback);
                    if (vol is null) continue;
                    past /= vol.Value;
                }
                ranked.Add((past, Math.Log(fwd.Value / fwdNow.Value)));
            }
            if (ranked.Count < MinNames) continue;

            int k = Math.Max(1, ranked.Count / 3);
            var byMom = ranked.OrderByDescending(r => r.Past).ToList();
            spreads.Add(byMom.Take(k).Average(r => r.Fwd) - byMom.TakeLast(k).Average(r => r.Fwd));
            pooled.Add(ranked.ToArray());
        }

        return (spreads, pooled);
    }

    private static void Report(List<Result> results, List<Series> series,
        DateTime start, DateTime end, bool volNorm, int permutations)
    {
        Console.WriteLine();

        int beatsRandom = results.Count(r => r.Top > r.Random);
        int beatsAll = results.Count(r => r.Top > r.All);
        Console.WriteLine($"  Configurations where TOP beats RANDOM: {beatsRandom} of {results.Count}.");
        Console.WriteLine($"  Configurations where TOP beats holding the whole basket: {beatsAll} of {results.Count}.");
        Console.WriteLine("  A plateau of positives is the signature of a real effect; one lone winner is a fit.");
        Console.WriteLine();
        Console.WriteLine("  ── by lookback (the axis the literature says should matter) ──");
        foreach (var g in results.GroupBy(r => r.Lookback).OrderBy(g => g.Key))
            Console.WriteLine($"    {g.Key,4}d lookback: beats random {g.Count(r => r.Top > r.Random)}/{g.Count()}   " +
                              $"mean excess {g.Average(r => r.Top - r.Random),+8:+0.0%;-0.0%;0}");
        Console.WriteLine("    Short lookbacks losing while long ones win is the documented shape —");
        Console.WriteLine("    one-month horizons carry short-term REVERSAL, not momentum.");
        Console.WriteLine();

        var best = results.OrderByDescending(r => r.Top - r.Random).First();
        Console.WriteLine($"  ── per-period test on look={best.Lookback} skip={best.Skip} hold={best.Hold} ──");

        // ── The null has to be the null of the STATISTIC WE COMPUTED ──────────
        //
        // `best` is the argmax over the 4x2x2 grid built above. Testing it against a
        // FIXED-configuration null answers "would THIS configuration produce this spread by
        // chance" — but the statistic actually computed is `max` over sixteen configurations,
        // and the maximum of sixteen draws is extreme far more often than any one of them.
        // The p was too small by roughly the effective number of independent cells.
        //
        // This is the p = 0.0045 recorded in Catalogue/edges.json for xs-momentum-equities —
        // the only edge with a full robustness pass — and quoted as the headline in
        // docs/XSMOMENTUM_FINDINGS.md. The file's own comment at the grid ("A plateau of
        // positives is the signature of a real effect; one lone winner is a fit") shows the
        // author understood the shape; the code still tested the winner.
        //
        // The fix is the standard max-statistic (maxT) null: build the per-period data for
        // EVERY cell, and on each permutation shuffle all of them and take the maximum, so the
        // reference distribution is the distribution of the maximum. Both numbers are printed,
        // because the difference between them is the size of the selection effect and is worth
        // seeing rather than silently absorbing.
        var cells = new List<(Result Cfg, List<double> Spreads, List<(double Past, double Fwd)[]> Pooled)>();
        foreach (var cfg in results)
        {
            var (sp, po) = PerPeriodSpreads(series, start, end, cfg, volNorm);
            if (sp.Count >= 8) cells.Add((cfg, sp, po));
        }

        var bestCell = cells.FirstOrDefault(c => ReferenceEquals(c.Cfg, best));
        if (bestCell.Spreads == null || bestCell.Spreads.Count < 8)
        {
            Console.WriteLine("    too few rebalances");
            return;
        }

        var spreads = bestCell.Spreads;
        var pooled = bestCell.Pooled;

        double mean = spreads.Average();
        double sd = Math.Sqrt(spreads.Sum(v => (v - mean) * (v - mean)) / (spreads.Count - 1));
        int positive = spreads.Count(v => v > 0);

        // ── The max statistic has to be STANDARDISED across cells ─────────────
        //
        // The first version of this correction took the maximum of the raw mean spread across
        // cells, and that comparison is not between like things. A hold=90 cell's per-period
        // spread is a 90-day return and a hold=30 cell's is a 30-day one, so the long-hold cells
        // are roughly three times larger in scale AND have a third as many periods — their null
        // spreads dominate the maximum whatever the data says. Run that way, the equity grid's
        // hold=30 winner was compared against a null built mostly out of hold=90 draws and came
        // back p = 0.97: not "no effect", but "wrong yardstick".
        //
        // Westfall-Young maxT is standardised for exactly this reason. The scale each cell is
        // divided by is that cell's OWN NULL dispersion, estimated from its permutation draws —
        // not its observed standard deviation. Dividing by the observed sd would be the textbook
        // t-statistic and it is the wrong choice here, because momentum-sorted thirds are more
        // volatile than randomly-sorted ones: the effect under test inflates the denominator and
        // the test loses most of its power to the thing it is looking for. Studentising by the
        // null instead puts every cell on one scale while leaving the test as sharp as the
        // original mean-based permutation, which is what makes the naive number below still
        // comparable to the p = 0.0045 recorded in edges.json.
        //
        // One pass, storing every permuted cell mean (cells x permutations doubles — a few MB),
        // because the null scale is not known until the permutations are finished.
        var permMeans = new double[cells.Count][];
        for (int c = 0; c < cells.Count; c++) permMeans[c] = new double[permutations];

        var rng = new Random(555);
        var permSpreads = new List<double>();
        for (int p = 0; p < permutations; p++)
        {
            for (int c = 0; c < cells.Count; c++)
            {
                var cell = cells[c];
                permSpreads.Clear();
                foreach (var arr in cell.Pooled)
                {
                    int k = Math.Max(1, arr.Length / 3);
                    var shuf = arr.OrderBy(_ => rng.Next()).ToArray();
                    permSpreads.Add(shuf.Take(k).Average(r => r.Fwd) - shuf.TakeLast(k).Average(r => r.Fwd));
                }
                double acc = 0; foreach (var v in permSpreads) acc += v;
                permMeans[c][p] = acc / permSpreads.Count;
            }
        }

        var nullMu = new double[cells.Count];
        var nullSd = new double[cells.Count];
        for (int c = 0; c < cells.Count; c++)
        {
            double m = 0; foreach (var v in permMeans[c]) m += v; m /= permutations;
            double ss = 0; foreach (var v in permMeans[c]) ss += (v - m) * (v - m);
            nullMu[c] = m;
            nullSd[c] = Math.Sqrt(ss / (permutations - 1));
        }

        double Z(int c, double value) => nullSd[c] > 0 ? Math.Abs(value - nullMu[c]) / nullSd[c] : 0.0;

        // The observed side of a maxT test is also a maximum: the largest standardised deviation
        // any cell in the grid attains. Testing only the return-best cell against a max null
        // would be conservative rather than wrong, but it would answer a question nobody asked —
        // the search ranged over the whole grid, so the statistic searched for is the grid's
        // largest.
        int selectedIdx = cells.FindIndex(c => ReferenceEquals(c.Cfg, best));
        double zBest = double.NegativeInfinity;
        Result? zBestCfg = null;
        for (int c = 0; c < cells.Count; c++)
        {
            double z = Z(c, cells[c].Spreads.Average());
            if (z > zBest) { zBest = z; zBestCfg = cells[c].Cfg; }
        }
        double zSelected = Z(selectedIdx, mean);

        int extreme = 0;            // naive: the return-best cell against its own null
        int extremeMax = 0;         // honest: the grid maximum against the null of the maximum
        for (int p = 0; p < permutations; p++)
        {
            double maxUnderNull = double.NegativeInfinity;
            for (int c = 0; c < cells.Count; c++)
            {
                double z = Z(c, permMeans[c][p]);
                if (z > maxUnderNull) maxUnderNull = z;
            }
            if (Z(selectedIdx, permMeans[selectedIdx][p]) >= zSelected) extreme++;
            if (maxUnderNull >= zBest) extremeMax++;
        }

        double pv = (extremeMax + 1.0) / (permutations + 1.0);        // the one the verdict uses
        double pvNaive = (extreme + 1.0) / (permutations + 1.0);

        Console.WriteLine($"    mean top−bottom spread per {best.Hold}d period: {mean:+0.00%;-0.00%;0}   " +
                          $"sd {sd:0.00%}   positive {positive}/{spreads.Count}   z vs null {zSelected:0.00}");
        Console.WriteLine($"    grid max |z| = {zBest:0.00} at look={zBestCfg!.Lookback} skip={zBestCfg.Skip} hold={zBestCfg.Hold}");
        Console.WriteLine($"    p = {pv:0.0000}  (max-statistic null over {cells.Count} grid cells, studentised by each cell's null)" +
                          (pv <= 0.05 ? "  *" : ""));
        Console.WriteLine($"    p = {pvNaive:0.0000}  (fixed-configuration null — POST-SELECTION, shown for contrast:");
        Console.WriteLine( "                  this cell was chosen as the grid maximum, so its own null is too narrow)");
        Console.WriteLine();

        Console.WriteLine("  ── VERDICT ──");

        // Judging on the flat grid count would be wrong: it includes lookbacks the literature says
        // should NOT work. One-month horizons carry short-term reversal, and counting their failure
        // against the hypothesis is counting a confirmed prediction as evidence against it. The
        // question is whether the LONG lookbacks work and whether the effect is monotone in lookback.
        var longLb = results.Where(r => r.Lookback >= 180).ToList();
        int longWins = longLb.Count(r => r.Top > r.Random);
        var byLb = results.GroupBy(r => r.Lookback).OrderBy(g => g.Key)
                          .Select(g => g.Average(r => r.Top - r.Random)).ToList();
        bool monotone = byLb.Count >= 3 && byLb.Last() > byLb.First()
                        && byLb.Last() == byLb.Max();

        if (longWins >= longLb.Count * 0.75 && pv <= 0.05)
        {
            Console.WriteLine($"    Cross-sectional momentum is PRESENT, concentrated where it should be:");
            Console.WriteLine($"    {longWins} of {longLb.Count} long-lookback configurations beat random selection, and the");
            Console.WriteLine($"    per-period spread clears its permutation null at p = {pv:0.0000}.");
            if (monotone)
                Console.WriteLine("    The excess rises monotonically with lookback — the documented shape, not a fit.");
            Console.WriteLine();
            Console.WriteLine("    Two things before this is tradeable: survivorship inflates the level (every name");
            Console.WriteLine("    here still trades, and the worst-ranked are exactly the ones that would have");
            Console.WriteLine("    delisted), and no transaction costs are modelled. Read the SPREAD, not the return.");

            XsMomentumRobustness.Run(series, start, end, best.Lookback, best.Skip, best.Hold, volNorm);
        }
        else if (longWins <= longLb.Count * 0.4 || pv > 0.2)
        {
            Console.WriteLine("    No cross-sectional momentum here. Ranking by trailing return does no better than");
            Console.WriteLine("    drawing names from a hat, which is the whole claim.");
        }
        else
        {
            Console.WriteLine($"    Mixed: {longWins} of {longLb.Count} long-lookback configurations beat random, per-period");
            Console.WriteLine($"    p = {pv:0.0000}. Suggestive, not decisive at this universe size.");
        }
    }

    private static List<Series> Load(string dir, string tf)
    {
        var outp = new List<Series>();
        foreach (var file in Directory.GetFiles(dir, $"*_{tf}.json")
                     .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f))
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            if (snap.Bars.Count < 400) continue;

            string cls = LabSnapshots.AssetClass(Path.GetFileName(file));
            if (cls == "skip") continue;

            outp.Add(new Series
            {
                Symbol = snap.Symbol,
                Class = cls,
                Dates = snap.Bars.Select(b => b.Date).ToArray(),
                Closes = snap.Bars.Select(b => b.Close).ToArray(),
            });
        }

        // Same instrument from two providers would be ranked twice and could occupy the top third
        // on its own. Keep the longest history per symbol.
        return outp.GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
                   .Select(g => g.OrderByDescending(s => s.Dates.Length).First())
                   .ToList();
    }
}
