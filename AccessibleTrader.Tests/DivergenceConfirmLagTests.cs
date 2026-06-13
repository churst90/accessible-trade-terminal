using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Xunit;

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

        // NOTE: a full provider-level causality test (compute on the full series, then on
        // each truncated prefix, and assert markers never vanish) is the ideal pin, but it
        // requires a synthetic series that trips Cipher B's depth(-35)/conviction(TR≥avg×0.85)
        // /twin-pivot gates — too brittle to construct deterministically. The causality
        // property holds by construction: detection runs at pivot p using wt1[p..p+pivotBars]
        // and ShiftMarkersForward moves the marker to exactly p+pivotBars, the first bar at
        // which all inputs exist. The mechanism tests above pin that shift. Empirical proof
        // that the bug inflated results lives in docs/ASSET_PROFILE_FINDINGS.md (round 12c).
    }
}
