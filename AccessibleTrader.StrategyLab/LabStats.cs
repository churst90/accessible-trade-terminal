namespace AccessibleTrader.StrategyLab;

/// <summary>
/// The statistics primitives the lab's commands share.
///
/// <para>
/// These existed as private copies — <c>PermutationP</c> in nine files, <c>RollingZ</c> in three —
/// and the copies had already drifted apart in ways nobody could see from any one file: six
/// distinct <c>PermutationP</c> bodies differing in seed, in whether they silently capped the
/// permutation count, and in one case in whether they bounds-checked the sum loop at all. A
/// statistics primitive is the last thing that should exist in nine versions, because a defect in
/// one becomes a wrong verdict in one report and a right verdict in the next, with nothing to
/// compare. Same reasoning as <see cref="StableSeed"/>, which was the same story about seeds.
/// </para>
///
/// <para>
/// The per-command seed and permutation cap stay at the call sites deliberately. They are
/// research parameters, not implementation details, and hiding them in here would make two
/// commands that disagree look like they agree.
/// </para>
/// </summary>
public static class LabStats
{
    /// <summary>
    /// Prints, once per command run, that <c>--permutations</c> was capped — if it was.
    ///
    /// <para>
    /// The cap is applied inside a loop over many buckets, so reporting it per test would be
    /// noise; reporting it nowhere, which is what happened until 2026-08-25, meant asking for
    /// 50,000 permutations quietly gave you 4,000 and a p-value floored at 1/4001 that read as
    /// if it came from the count you asked for. A research tool that silently does less work
    /// than requested is producing numbers its own operator cannot interpret.
    /// </para>
    /// </summary>
    public static void ReportPermutationCap(string command, int requested, int cap)
    {
        if (requested <= cap) return;
        System.Console.WriteLine(
            $"note: --permutations {requested:N0} capped at {cap:N0} for `{command}` — the test runs " +
            $"once per bucket here, so the full count would dominate runtime. p-values floor at " +
            $"{1.0 / (cap + 1):G3}.");
        System.Console.WriteLine();
    }

    /// <summary>
    /// Two-sample permutation test on the difference of means: shuffle the pooled values, split
    /// them into groups of the original sizes, and count how often the random split produces a gap
    /// at least as extreme as the observed one.
    ///
    /// <para>
    /// Two-sided — the comparison is on absolute gaps, so a result that is extreme in the
    /// *opposite* direction to the claim also counts against it. That is the honest form when the
    /// question is "do these two groups differ" rather than "is A better than B".
    /// </para>
    ///
    /// <para>
    /// The +1 on both numerator and denominator is the standard correction: with a finite number of
    /// permutations, p = 0 is a claim the data cannot support, so the floor is 1/(runs+1).
    /// </para>
    /// </summary>
    /// <param name="pool">All observations from both groups.</param>
    /// <param name="nA">Size of the first group.</param>
    /// <param name="nB">Size of the second group.</param>
    /// <param name="observed">The real difference of means, whose sign is ignored.</param>
    /// <param name="runs">Permutations requested.</param>
    /// <param name="seed">Fixed by the caller so a rerun reproduces. See <see cref="StableSeed"/>.</param>
    /// <param name="cap">
    /// Optional ceiling on <paramref name="runs"/>, for commands where the test is inside a loop
    /// over many buckets and the full count would dominate the runtime. Reported back through
    /// <paramref name="runsUsed"/> so a caller can print what was actually done rather than what
    /// was asked for.
    /// </param>
    /// <param name="runsUsed">The permutation count actually executed.</param>
    public static double PermutationP(
        double[] pool, int nA, int nB, double observed, int runs, int seed, int? cap, out int runsUsed)
    {
        runsUsed = cap is { } c ? System.Math.Min(runs, c) : runs;
        if (nA <= 0 || nB <= 0 || runsUsed <= 0) return 1.0;

        var rng = new Random(seed);
        var work = (double[])pool.Clone();
        int extreme = 0;

        for (int p = 0; p < runsUsed; p++)
        {
            for (int i = work.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (work[i], work[j]) = (work[j], work[i]);
            }

            double a = 0, b = 0;
            for (int i = 0; i < nA && i < work.Length; i++) a += work[i];
            for (int i = nA; i < nA + nB && i < work.Length; i++) b += work[i];
            if (System.Math.Abs(a / nA - b / nB) >= System.Math.Abs(observed)) extreme++;
        }
        return (extreme + 1.0) / (runsUsed + 1.0);
    }

    /// <inheritdoc cref="PermutationP(double[], int, int, double, int, int, int?, out int)"/>
    public static double PermutationP(
        double[] pool, int nA, int nB, double observed, int runs, int seed, int? cap = null) =>
        PermutationP(pool, nA, nB, observed, runs, seed, cap, out _);

    /// <summary>
    /// A two-sample permutation test for pools whose rows OVERLAP in time.
    ///
    /// <para>
    /// ── Why the plain test is wrong here ───────────────────────────────────────
    /// <see cref="PermutationP(double[], int, int, double, int, int, int?, out int)"/> shuffles
    /// individual rows, which assumes they are exchangeable. They are not when each row is a
    /// forward return over <c>horizon</c> bars emitted once per bar: consecutive rows share
    /// <c>horizon − 1</c> of their forward bars, and the underlying returns are autocorrelated
    /// on top of that. The effective sample size is closer to <c>n / horizon</c> than to
    /// <c>n</c>, so the null is far too narrow and <b>significance is inflated by roughly
    /// √horizon</b>. A p of 0.004 computed that way is not a p of 0.004.
    /// </para>
    ///
    /// <para>
    /// This affected every permutation test in the lab that emits one observation per bar over
    /// a multi-bar horizon — <c>OnChainCommand</c> and <c>VolumeCommand</c> at horizon 20,
    /// <c>PocDeviationCommand</c>, <c>EventsCommand</c>, <c>GateCommand</c> — including
    /// <c>volume-informative-crypto</c> (p = 0.004) and <c>poc-mean-reversion-equities</c>
    /// (p = 0.0004 on n = 348,000, where the n is the ROW count and not the
    /// independent-observation count).
    /// </para>
    ///
    /// <para>
    /// ── What this does instead ─────────────────────────────────────────────────
    /// Shuffles CONTIGUOUS BLOCKS of <paramref name="blockSize"/> rows rather than individual
    /// rows, so whatever dependence exists inside a block survives into the null. Blocks of at
    /// least the horizon are what make two blocks genuinely non-overlapping. This keeps every
    /// row — the alternative the finding also offers, sampling every horizon-th bar, throws
    /// away 95% of the data at horizon 20.
    /// </para>
    ///
    /// <para><b>The pool must be in TIME ORDER</b>, group A then group B, or the blocks are not
    /// contiguous in time and this degrades to the plain test with extra steps.</para>
    /// </summary>
    /// <param name="blockSize">
    /// Rows per block — at least the forward horizon. One or less means the rows do not
    /// overlap, and this falls through to the plain row-wise test.
    /// </param>
    public static double BlockPermutationP(
        double[] pool, int nA, int nB, double observed, int runs, int seed, int blockSize,
        int? cap, out int runsUsed)
    {
        if (blockSize <= 1)
            return PermutationP(pool, nA, nB, observed, runs, seed, cap, out runsUsed);

        runsUsed = cap is { } c ? System.Math.Min(runs, c) : runs;
        if (nA <= 0 || nB <= 0 || runsUsed <= 0) { runsUsed = System.Math.Max(0, runsUsed); return 1.0; }

        // Partition the time-ordered pool into contiguous blocks. The tail block may be short;
        // dropping it would bias the null toward whichever group the series ends in.
        var blocks = new List<double[]>();
        for (int i = 0; i < pool.Length; i += blockSize)
            blocks.Add(pool[i..System.Math.Min(i + blockSize, pool.Length)]);

        // Two blocks is not a permutation test. Say so by returning 1.0 rather than a p that
        // looks computed: at that point the data cannot distinguish anything.
        if (blocks.Count < 4) return 1.0;

        var rng = new Random(seed);
        var order = new int[blocks.Count];
        var work = new double[pool.Length];
        int extreme = 0;

        for (int p = 0; p < runsUsed; p++)
        {
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            int w = 0;
            foreach (int bi in order)
            {
                var b = blocks[bi];
                System.Array.Copy(b, 0, work, w, b.Length);
                w += b.Length;
            }

            double a = 0, bsum = 0;
            for (int i = 0; i < nA && i < work.Length; i++) a += work[i];
            for (int i = nA; i < nA + nB && i < work.Length; i++) bsum += work[i];
            if (System.Math.Abs(a / nA - bsum / nB) >= System.Math.Abs(observed)) extreme++;
        }

        return (extreme + 1.0) / (runsUsed + 1.0);
    }

    /// <inheritdoc cref="BlockPermutationP(double[], int, int, double, int, int, int, int?, out int)"/>
    public static double BlockPermutationP(
        double[] pool, int nA, int nB, double observed, int runs, int seed, int blockSize,
        int? cap = null) =>
        BlockPermutationP(pool, nA, nB, observed, runs, seed, blockSize, cap, out _);


    /// <summary>
    /// Rolling z-score of <paramref name="v"/> over a trailing window that INCLUDES the current
    /// bar, which is what makes it usable as a signal: the value at bar i is derived from bars
    /// i-win..i and never from anything later.
    ///
    /// <para>
    /// NaN inputs are skipped rather than propagated, because the analytics metrics these run over
    /// are published on their own calendars and are full of holes. A window that is more than half
    /// holes yields NaN instead of a z-score computed from a handful of points, and a window with
    /// no variance yields NaN rather than a division that would report every point as infinitely
    /// unusual.
    /// </para>
    /// </summary>
    public static double[] RollingZ(double[] v, int win)
    {
        var z = new double[v.Length];
        Array.Fill(z, double.NaN);
        for (int i = win; i < v.Length; i++)
        {
            double sum = 0, sumSq = 0;
            int n = 0;
            for (int j = i - win; j <= i; j++)
            {
                if (double.IsNaN(v[j])) continue;
                sum += v[j];
                sumSq += v[j] * v[j];
                n++;
            }
            if (n < win / 2) continue;
            double mean = sum / n, var = sumSq / n - mean * mean;
            if (var > 1e-12) z[i] = (v[i] - mean) / System.Math.Sqrt(var);
        }
        return z;
    }
}
