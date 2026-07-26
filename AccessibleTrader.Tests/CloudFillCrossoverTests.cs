using AccessibleTrader.Core.Services.Rendering;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The crossover interpolation that closes the cloud-fill gap: where the upper and
    /// lower lines cross between two bars, both the ending and starting fill runs share
    /// that exact point as an apex, so MA Cloud / Ichimoku / WaveTrend fills are
    /// continuous instead of leaving an unfilled triangle at every crossing.
    /// </summary>
    public class CloudFillCrossoverTests
    {
        [Fact]
        public void Symmetric_cross_is_at_the_midpoint()
        {
            // upper 2→ -2, lower -2→2: they cross exactly halfway.
            Assert.Equal(0.5, StandardRenderers.CloudCrossoverT(2, -2, -2, 2), 6);
        }

        [Fact]
        public void Cross_near_the_left_bar_gives_small_t()
        {
            // Gap collapses fast: d1 tiny (0.1), d2 large negative (-9.9) → t ≈ 0.01.
            var t = StandardRenderers.CloudCrossoverT(0.1, 0, -9.9, 0);
            Assert.True(t is > 0 and < 0.05, $"expected small t, got {t}");
        }

        [Fact]
        public void Cross_near_the_right_bar_gives_large_t()
        {
            var t = StandardRenderers.CloudCrossoverT(9.9, 0, -0.1, 0);
            Assert.True(t is > 0.95 and < 1.0, $"expected large t, got {t}");
        }

        [Fact]
        public void Result_is_always_clamped_to_unit_interval()
        {
            // Degenerate / same-sign inputs must never produce an out-of-range apex.
            Assert.InRange(StandardRenderers.CloudCrossoverT(5, 5, 5, 5), 0.0, 1.0);
            Assert.InRange(StandardRenderers.CloudCrossoverT(1, 0, 2, 0), 0.0, 1.0);
        }
    }
}
