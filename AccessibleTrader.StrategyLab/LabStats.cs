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
