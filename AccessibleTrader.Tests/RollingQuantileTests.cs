using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// First coverage for <see cref="RollingQuantile"/> — the causal rolling-quantile helper every
    /// adaptive-threshold indicator sits on.
    ///
    /// <para>
    /// ── Why this file exists ───────────────────────────────────────────────────
    /// A2d/D09: replacing <c>if (count &lt; warmupMin) continue;</c> with <c>count &lt; 1</c> left
    /// the full suite green. The type census explains it — <c>RollingQuantile</c> was named
    /// nowhere in either test project, so the whole class was reachable only by accident, through
    /// tests aimed at the oscillators that call it.
    /// </para>
    ///
    /// <para>
    /// The warmup is not decoration. A percentile computed from one sample IS that sample, so a
    /// "95th percentile of the last 200 bars" threshold starts life equal to the first value it
    /// ever saw, and every adaptive gate reading it fires on bar one against a threshold that
    /// means nothing. The failure is silent: a number is produced, it is the wrong number, and
    /// nothing downstream can tell the difference between "not warmed up" and "warmed up and
    /// this is the answer" once the NaN is gone.
    /// </para>
    ///
    /// <para>
    /// The look-ahead test is the other half. The class documents itself as causal — at bar i only
    /// [i-N+1 .. i] may be visible — and that claim is exactly the one that cannot be spotted by
    /// reading a chart, because a look-ahead quantile looks better, not broken.
    /// </para>
    /// </summary>
    public class RollingQuantileTests
    {
        private static double[] Ramp(int n)
        {
            var v = new double[n];
            for (int i = 0; i < n; i++) v[i] = i + 1;
            return v;
        }

        [Fact]
        public void NothingIsEmittedUntilWarmupMinSamplesExist()
        {
            var q = RollingQuantile.Compute(Ramp(20), window: 10, probability: 0.5, warmupMin: 5);

            for (int i = 0; i < 4; i++)
                Assert.True(double.IsNaN(q[i]), $"index {i} emitted {q[i]} from only {i + 1} sample(s)");

            // The fifth sample is the first legal one.
            Assert.False(double.IsNaN(q[4]));
            Assert.All(q[4..], v => Assert.False(double.IsNaN(v)));
        }

        [Fact]
        public void SkippedNaNsDoNotCountTowardsWarmup()
        {
            // Three holes, then real data. The fifth REAL sample lands at index 7.
            var src = new[] { double.NaN, double.NaN, double.NaN, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0 };

            var q = RollingQuantile.Compute(src, window: 10, probability: 0.5, warmupMin: 5);

            for (int i = 0; i < 7; i++)
                Assert.True(double.IsNaN(q[i]), $"index {i} emitted {q[i]} before five real samples existed");
            Assert.False(double.IsNaN(q[7]));
        }

        [Fact]
        public void TheQuantileIsInterpolatedBetweenTheBracketingSamples()
        {
            // Window [1..5], p=0.5 → the median, 3. p=0.75 → rank 3.0 → exactly 4.
            // p=0.6 → rank 2.4 → 3 + 0.4*(4-3) = 3.4, which is the interpolation itself.
            var sorted = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
            Assert.Equal(3.0, RollingQuantile.Percentile(sorted, 5, 0.5), 9);
            Assert.Equal(4.0, RollingQuantile.Percentile(sorted, 5, 0.75), 9);
            Assert.Equal(3.4, RollingQuantile.Percentile(sorted, 5, 0.60), 9);
            Assert.Equal(1.0, RollingQuantile.Percentile(sorted, 5, 0.0), 9);
            Assert.Equal(5.0, RollingQuantile.Percentile(sorted, 5, 1.0), 9);
        }

        [Fact]
        public void AValueAtBarIDoesNotChangeWhenLaterBarsArrive()
        {
            // The causality claim in the class summary, tested the only way it can be: compute
            // over a prefix, then over the whole series, and require the shared indices to agree.
            // A quantile that peeked forward would move when the future was added.
            var full = Ramp(30);
            var whole = RollingQuantile.Compute(full, window: 8, probability: 0.9, warmupMin: 4);

            for (int cut = 10; cut <= 30; cut += 5)
            {
                var prefix = RollingQuantile.Compute(full[..cut], window: 8, probability: 0.9, warmupMin: 4);
                for (int i = 0; i < cut; i++)
                    Assert.Equal(whole[i], prefix[i], 9);
            }
        }

        [Fact]
        public void ADegenerateWindowEmitsNothingRatherThanGuessing()
        {
            Assert.All(RollingQuantile.Compute(Ramp(10), window: 1, probability: 0.5, warmupMin: 1),
                       v => Assert.True(double.IsNaN(v)));
            Assert.All(RollingQuantile.Compute(Ramp(10), window: 5, probability: 0.5, warmupMin: 0),
                       v => Assert.True(double.IsNaN(v)));
            Assert.Empty(RollingQuantile.Compute(Array.Empty<double>(), 5, 0.5, 1));
        }
    }
}
