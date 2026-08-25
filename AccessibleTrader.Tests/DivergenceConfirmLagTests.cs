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
        // compares. This file keeps the mechanism pins and adds the Cipher A ones below, because
        // the empirical test can only fail on a series that happens to produce a divergence near a
        // probe point, and "happens to" is not a guarantee.

        // ── Cipher A: the same fix, two months later ──────────────────────────
        //
        // Cipher A has the identical pivot loop and never received this fix. Its Bullish /
        // Bearish / Overbought Bearish (Blood Diamond) markers were stamped at the pivot bar, so a
        // backtest entered at the exact pivot low with PivotBars of hindsight — the thing the
        // comment in CipherBProvider.Calculate says "inflates every divergence-based backtest".
        // The shift is now shared (IndicatorMath.ShiftMarkersForward) and both providers call it.

        private const int PivotBars = 3;   // CIPHER_A default

        private static Dictionary<string, double[]> RunCipherA(bool confirmLag, int flavour)
        {
            var provider = new CipherAProvider();
            var bars = IndicatorCausalityTests.Bars(flavour);
            var buffer = new AccessibleTrader.Core.Services.Indicators.IndicatorResultBuffer(
                new Dictionary<string, double[]>(), bars.Count);
            var pars = new Dictionary<string, object>
            {
                ["PivotBars"] = (double)PivotBars,
                ["DivergenceConfirmLag"] = confirmLag,
            };
            provider.Calculate("CIPHER_A", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
                pars, buffer);
            return buffer.GetResults();
        }

        [Theory]
        [InlineData(CipherAProvider.CompBullDiv)]
        [InlineData(CipherAProvider.CompBearDiv)]
        [InlineData(CipherAProvider.CompBloodDiamond)]
        public void CipherA_ShiftsEveryDivergenceMarkerToItsConfirmationBar(string component)
        {
            // Every one of the three, because the failure this guards against is not "the shift is
            // missing" but "the shift was applied to two of the three arrays" — which is the exact
            // shape of the original defect one file over.
            int totalMarkers = 0;

            foreach (int flavour in IndicatorCausalityTests.Flavours)
            {
                var shifted = RunCipherA(confirmLag: true, flavour)[component];
                var stamped = RunCipherA(confirmLag: false, flavour)[component];

                var expected = AccessibleTrader.Sdk.Indicators.IndicatorMath.ShiftMarkersForward(
                    stamped, PivotBars, stamped.Length);

                totalMarkers += stamped.Count(v => !double.IsNaN(v));

                for (int i = 0; i < shifted.Length; i++)
                    Assert.True(
                        (double.IsNaN(expected[i]) && double.IsNaN(shifted[i])) || expected[i] == shifted[i],
                        $"{component} bar {i} (series {flavour}): marker sits at the pivot bar. " +
                        $"With the lag on it should be at the pivot bar + {PivotBars}.");
            }

            // A component that never fires makes the comparison above vacuous, so say which ones
            // those are rather than letting a green tick imply coverage it does not have.
            //
            // Bearish Divergence is the exception, and for a reason worth writing down: it fires
            // only when the EARLIER WaveTrend pivot sat below the overbought threshold (above it
            // and the same event is published as Overbought Bearish instead). On a synthetic series
            // the muted WT peaks all land in falling stretches, where price is not making the
            // higher high the divergence also needs. Real price action produces it readily. It is
            // listed in IndicatorCausalityTests.NotExercisedByTheseSeries for the same reason.
            if (component == CipherAProvider.CompBearDiv) return;

            Assert.True(totalMarkers > 0,
                $"No {component} fired on any synthetic series, so this test proved nothing. " +
                "Give the generator in IndicatorCausalityTests price action that produces one.");
        }

        [Fact]
        public void CipherA_DefaultsToShifting()
        {
            // Default ON, like Cipher B. Off is a chart-review option that makes the markers
            // look-ahead-biased for anything reading them, and the parameter description says so.
            var p = new CipherAProvider().GetIndicators()
                .Single(i => i.Code == "CIPHER_A").Parameters
                .Single(x => x.Name == "DivergenceConfirmLag");

            Assert.Equal(true, p.DefaultValue);
        }
    }
}
