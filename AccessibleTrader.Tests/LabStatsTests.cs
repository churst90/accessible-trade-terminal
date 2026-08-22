using System;
using System.Linq;
using AccessibleTrader.StrategyLab;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Coverage for <see cref="LabStats"/> — the permutation test and the rolling z-score that the
/// lab's commands share.
///
/// <para>
/// These are new only as a *location*. The code has been deciding research verdicts for a long
/// time, as nine private <c>PermutationP</c> copies and three <c>RollingZ</c> copies, none of them
/// tested. Consolidating them is what makes them testable at all — and it is also what made the
/// drift visible: six distinct <c>PermutationP</c> bodies, differing in seed, in whether they
/// silently capped the permutation count, and in one case in whether they bounds-checked the sum
/// loop.
/// </para>
///
/// <para>
/// The tests below lean on the cases where the answer is exact rather than statistical — p is
/// exactly 1.0 when the observed gap is zero, exactly 1/(runs+1) when nothing can match it, and a
/// linear ramp has an exactly computable z-score. An exact expectation is the difference between a
/// test that pins the arithmetic and a test that pins whatever the code happened to produce.
/// </para>
/// </summary>
public class LabStatsTests
{
    // ── PermutationP ───────────────────────────────────────────────────

    /// <summary>
    /// The floor. Nothing can be at least as extreme as an infinite observed gap, so no permutation
    /// counts, and the answer is the +1-corrected minimum — 1/(runs+1), never 0. A p-value of
    /// exactly zero is a claim no finite number of permutations can support, and printing one is
    /// how a lab overstates its own certainty.
    /// </summary>
    [Fact]
    public void PermutationP_NeverReturnsZero_AndFloorsAtOneOverRunsPlusOne()
    {
        double p = LabStats.PermutationP(
            Pool(20), nA: 10, nB: 10, observed: double.PositiveInfinity, runs: 99, seed: 1);

        Assert.Equal(0.01, p, 12);
    }

    /// <summary>
    /// The ceiling, and the direction of the comparison. With an observed gap of zero, *every*
    /// permutation is at least as extreme, so p is exactly 1.0 — no evidence, which is the honest
    /// reading. A strict <c>&gt;</c> instead of <c>&gt;=</c> would report 1/(runs+1) here: the most
    /// significant result the test can produce, from data showing nothing at all.
    ///
    /// <para>
    /// The pool is deliberately all-identical. On a pool with spread, a shuffled gap is never
    /// *exactly* zero, so <c>&gt;</c> and <c>&gt;=</c> agree and the test cannot tell them apart —
    /// which is what it did before this note was written. A tie has to be reachable for a test
    /// about tie-breaking to mean anything.
    /// </para>
    /// </summary>
    [Fact]
    public void PermutationP_ZeroObservedGap_IsExactlyOne()
    {
        var tied = Enumerable.Repeat(1.0, 20).ToArray();

        double p = LabStats.PermutationP(tied, nA: 10, nB: 10, observed: 0.0, runs: 49, seed: 2);

        Assert.Equal(1.0, p, 12);
    }

    /// <summary>
    /// Two-sided: the sign of the observed gap is discarded. A result extreme in the direction
    /// opposite to the claim still counts against the null, which is what "do these two groups
    /// differ" means. Asserted as exact equality between the two signs rather than as an
    /// approximation, because the same seed drives both.
    /// </summary>
    [Fact]
    public void PermutationP_IsTwoSided()
    {
        var pool = Pool(40);

        double positive = LabStats.PermutationP(pool, 20, 20, observed: 0.75, runs: 500, seed: 3);
        double negative = LabStats.PermutationP(pool, 20, 20, observed: -0.75, runs: 500, seed: 3);

        Assert.Equal(positive, negative, 12);
    }

    /// <summary>
    /// A cleanly separated sample must land near the floor. This is the only assertion here that is
    /// statistical rather than exact, and it is the one that would catch a test which is arithmetic
    /// all the way down and never actually discriminates.
    /// </summary>
    [Fact]
    public void PermutationP_PerfectlySeparatedGroups_AreSignificant()
    {
        var pool = Enumerable.Repeat(1.0, 12).Concat(Enumerable.Repeat(9.0, 12)).ToArray();

        double p = LabStats.PermutationP(pool, 12, 12, observed: -8.0, runs: 2000, seed: 4);

        Assert.True(p < 0.01, $"a perfectly separated sample scored p = {p}");
    }

    /// <summary>
    /// The cap applies to the denominator as well as to the loop. Capping the number of
    /// permutations run while still dividing by the number requested would understate every
    /// p-value from the four commands that cap — quietly, and in the direction that manufactures
    /// significance.
    /// </summary>
    [Fact]
    public void PermutationP_Cap_LimitsTheRunsAndTheDenominator()
    {
        double p = LabStats.PermutationP(
            Pool(30), 15, 15, observed: 0.5, runs: 10_000, seed: 5, cap: 100, out int used);

        Assert.Equal(100, used);
        // Every attainable p is k/101 for integer k, which is only true if the denominator capped too.
        Assert.Equal(Math.Round(p * 101), p * 101, 9);
    }

    /// <summary>
    /// An empty group means there is nothing to compare and the answer is "no evidence".
    ///
    /// <para>
    /// This is a latent bug the consolidation removed. Every private copy computed <c>a / nA</c>
    /// with no guard, so <c>nA = 0</c> gave NaN, and <c>NaN &gt;= x</c> is false — so no
    /// permutation ever counted as extreme and the function returned its *floor*. An empty group
    /// produced the most significant p-value the test can report. Every current call site gates on
    /// a minimum count, so nothing in the archive was affected, but the failure direction was
    /// exactly backwards.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(0, 0)]
    public void PermutationP_EmptyGroup_ReportsNoEvidenceRatherThanTotalSignificance(int nA, int nB)
    {
        double p = LabStats.PermutationP(Pool(20), nA, nB, observed: 5.0, runs: 999, seed: 6);

        Assert.Equal(1.0, p, 12);
    }

    /// <summary>
    /// The caller's array is not shuffled underneath it. The commands run several tests off one
    /// pooled array — the crowding command runs three — and an in-place shuffle would silently
    /// reorder the input to every test after the first.
    /// </summary>
    [Fact]
    public void PermutationP_DoesNotMutateTheCallersPool()
    {
        var pool = Pool(30);
        var before = (double[])pool.Clone();

        LabStats.PermutationP(pool, 15, 15, observed: 0.4, runs: 250, seed: 7);

        Assert.Equal(before, pool);
    }

    /// <summary>
    /// Same seed, same answer; different seed, different answer. The first half is what makes a
    /// stored verdict reproducible; the second is what stops a hard-coded seed inside the helper
    /// from satisfying the first. See <c>StableSeed</c> for how the callers derive theirs.
    /// </summary>
    [Fact]
    public void PermutationP_IsReproducibleForASeedAndSensitiveToIt()
    {
        var pool = Pool(60);

        double a = LabStats.PermutationP(pool, 30, 30, observed: 0.20, runs: 400, seed: 11);
        double b = LabStats.PermutationP(pool, 30, 30, observed: 0.20, runs: 400, seed: 11);
        double c = LabStats.PermutationP(pool, 30, 30, observed: 0.20, runs: 400, seed: 12);

        Assert.Equal(a, b, 12);
        Assert.NotEqual(a, c, 12);
    }

    // ── RollingZ ───────────────────────────────────────────────────────

    /// <summary>
    /// Known input, known output. On a linear ramp the trailing window of <c>win + 1</c> consecutive
    /// integers has mean at its centre and population variance <c>((win+1)² - 1) / 12</c>, so the
    /// current bar — always the window's maximum — sits an exactly computable distance above it.
    /// For win = 10 that is 5 / sqrt(10) ≈ 1.5811.
    ///
    /// <para>
    /// The population form (not the sample n-1 form) is what the code uses, and the two differ by
    /// 5% at this window size — enough to move a z-score across a reporting threshold and not
    /// enough for anyone to notice by eye.
    /// </para>
    /// </summary>
    [Fact]
    public void RollingZ_LinearRamp_HasAnExactlyComputableScore()
    {
        var v = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();

        var z = LabStats.RollingZ(v, win: 10);

        double expected = 5.0 / Math.Sqrt(10.0);
        for (int i = 10; i < v.Length; i++)
            Assert.Equal(expected, z[i], 9);
    }

    /// <summary>
    /// Everything before the first full window is NaN rather than a score computed from however
    /// many bars happen to exist. A z-score from four points is not a z-score, and a number is far
    /// more dangerous here than a hole — it gets plotted, bucketed and reported.
    /// </summary>
    [Fact]
    public void RollingZ_WarmupBarsAreNaN()
    {
        var z = LabStats.RollingZ(Enumerable.Range(0, 50).Select(i => (double)i).ToArray(), win: 20);

        for (int i = 0; i < 20; i++) Assert.True(double.IsNaN(z[i]), $"bar {i} should be NaN");
        Assert.False(double.IsNaN(z[20]));
    }

    /// <summary>
    /// A window with no variance yields NaN, not a division by something near zero. A flat stretch
    /// is common in the analytics metrics these run over — a daily series republished unchanged, a
    /// provider returning its last value — and an unguarded divide turns it into a z-score of
    /// several thousand, which is exactly the shape that reads as a discovery.
    /// </summary>
    [Fact]
    public void RollingZ_ConstantSeries_IsAllNaN_NotInfinite()
    {
        var z = LabStats.RollingZ(Enumerable.Repeat(42.0, 60).ToArray(), win: 20);

        Assert.All(z, x => Assert.True(double.IsNaN(x), $"expected NaN, got {x}"));
    }

    /// <summary>
    /// A variance below the noise floor yields NaN. This is the guard the constant-series test
    /// above cannot reach: on an exactly flat window the variance is exactly 0 and the score is
    /// 0/0 = NaN whether or not the threshold exists. It takes a variance that is tiny but real —
    /// a metric republished with float-level jitter — for the threshold to be the thing making the
    /// decision, and without it those bars score ±1 as if the jitter were signal.
    /// </summary>
    [Fact]
    public void RollingZ_VarianceBelowTheNoiseFloor_IsNaN()
    {
        // Alternating by 1e-8 around 0.001: true variance 2.5e-17, well under the 1e-12 floor,
        // and small enough in magnitude that the naive sumSq formula loses nothing to cancellation.
        var v = Enumerable.Range(0, 60).Select(i => 0.001 + (i % 2) * 1e-8).ToArray();

        var z = LabStats.RollingZ(v, win: 20);

        Assert.All(z, x => Assert.True(double.IsNaN(x), $"expected NaN, got {x}"));
    }

    /// <summary>
    /// NaN holes are skipped, but a window that is more than half holes yields NaN rather than a
    /// score built from a handful of surviving points. The metrics these run over publish on their
    /// own calendars — weekly, or on filing dates — so a daily-aligned series is mostly holes by
    /// construction, and the threshold is what stops a weekly metric being scored as if it were
    /// daily.
    /// </summary>
    [Fact]
    public void RollingZ_WindowThatIsMostlyHoles_IsNaN()
    {
        var v = Enumerable.Range(0, 80).Select(i => i % 4 == 0 ? (double)i : double.NaN).ToArray();

        var z = LabStats.RollingZ(v, win: 20);   // ~5 real points in each 21-bar window, floor is 10

        Assert.All(z, x => Assert.True(double.IsNaN(x), $"expected NaN, got {x}"));
    }

    /// <summary>
    /// **Causality.** The score at bar i must be computable from bars 0..i alone. Everything the
    /// lab does with a rolling z — bucketing forward returns by it, gating an entry on it — is only
    /// valid if the value that existed at bar i is the value being used, and the standing rule in
    /// this repo is that a look-ahead is proven by prefix, not by reading the loop bounds.
    /// </summary>
    [Fact]
    public void RollingZ_UsesNoBarLaterThanTheOneItScores()
    {
        var full = Series(300);
        var z = LabStats.RollingZ(full, win: 30);

        foreach (int cut in new[] { 60, 137, 200, 299 })
        {
            var prefixZ = LabStats.RollingZ(full.Take(cut + 1).ToArray(), win: 30);
            for (int i = 0; i <= cut; i++)
                Assert.Equal(z[i], prefixZ[i], 12);
        }
    }

    // ── Fixtures ───────────────────────────────────────────────────────

    /// <summary>A deterministic pool with real spread, so a permutation actually rearranges something.</summary>
    private static double[] Pool(int n)
    {
        var rng = new Random(2026);
        return Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
    }

    /// <summary>A deterministic wandering series with changing level and volatility.</summary>
    private static double[] Series(int n)
    {
        var rng = new Random(99);
        var v = new double[n];
        double level = 100;
        for (int i = 0; i < n; i++)
        {
            level += (rng.NextDouble() - 0.5) * (i < n / 2 ? 1.0 : 4.0);
            v[i] = level;
        }
        return v;
    }
}
