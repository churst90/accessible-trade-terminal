using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Regression pins for the 2026-06-12 divergence look-ahead fix. Cipher B confirms a
    /// WaveTrend pivot at bar p using the right shoulder wt1[p+1..p+pivotBars] but used to
    /// stamp the divergence marker at p, so a strategy reading it at p was trading on future
    /// bars. DivergenceConfirmLag (now default ON) shifts the marker to the confirmation bar.
    /// </summary>
    public class DivergenceConfirmLagTests
    {
        // ── Shift mechanism ───────────────────────────────────────────────────

        [Fact]
        public void Shift_MovesMarkersForwardByLag()
        {
            var src = new[] { double.NaN, 5.0, double.NaN, double.NaN, 7.0, double.NaN };
            var shifted = CipherBProvider.ShiftMarkersForward(src, lag: 2, n: src.Length);

            // 5.0 at idx1 → idx3; 7.0 at idx4 → idx6 (out of bounds, dropped).
            Assert.True(double.IsNaN(shifted[1]));
            Assert.Equal(5.0, shifted[3]);
            Assert.True(double.IsNaN(shifted[4]));
            Assert.True(shifted.Count(v => !double.IsNaN(v)) == 1); // the out-of-range one dropped
        }

        [Fact]
        public void Shift_ZeroLag_IsIdentity()
        {
            var src = new[] { double.NaN, 1.0, 2.0 };
            var shifted = CipherBProvider.ShiftMarkersForward(src, lag: 0, n: src.Length);
            Assert.Equal(src, shifted);
        }

        [Fact]
        public void Shift_DropsMarkersWhoseConfirmationFallsPastEnd()
        {
            // A pivot in the final `lag` bars cannot be confirmed in-sample, so its marker
            // is dropped rather than clamped — never invented at a bar it couldn't reach.
            var src = new[] { double.NaN, double.NaN, 9.0, 9.5 };
            var shifted = CipherBProvider.ShiftMarkersForward(src, lag: 3, n: src.Length);
            Assert.All(shifted, v => Assert.True(double.IsNaN(v)));
        }

        // The provider-level property — that a marker never appears at a bar whose confirmation
        // has not happened — is now checked empirically for every provider by
        // IndicatorCausalityTests, which runs each one over a prefix and over the full series and
        // compares.
        //
        // Cipher A's divergence pins lived here until 2026-10-17, when Cipher A was rebuilt from
        // measurements of real Market Cipher A, which draws no divergences: the three components
        // went (SignalCatalog.Retired says so to a strategy that named one). The shared shift they
        // pinned is still Cipher B's, pinned above.
    }
}
