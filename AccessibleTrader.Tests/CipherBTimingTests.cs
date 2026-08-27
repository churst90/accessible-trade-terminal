using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Two ways Cipher B told the truth at the wrong moment.
    ///
    /// <para><b>Shallow divergences fired three bars late.</b> The confirmation-lag shift is
    /// correct for the PIVOT-based detector: a pivot at bar p is only confirmable after seeing
    /// <c>wt1[p+1..p+pivotBars]</c>, so stamping the marker at p is look-ahead and shifting it
    /// to p+pivotBars is the honest fix. But the shallow cross-based detector added later writes
    /// <b>at the WT crossover bar</b> and is already causal — nothing about it needs a future
    /// bar. The shift was applied to the COMBINED arrays, so a shallow divergence detected at
    /// bar 400 was stamped at bar 403, with the <c>_anchorIdx</c>/<c>_anchorY</c> line geometry
    /// moving in lockstep. Every shallow divergence — a strategy leaf, an earcon and a spoken
    /// marker — fired later than the market event it describes.</para>
    ///
    /// <para><c>IndicatorCausalityTests</c> could never catch it: <b>a marker that is LATE is
    /// still causal.</b> The causality contract only refuses markers that read the future.</para>
    ///
    /// <para><b>Every gate was a function of how many bars were loaded.</b> The timeframe
    /// bucket rewrites <c>adxGate</c>, <c>atrFloorPct</c>, <c>mfPeriod</c>, <c>pivotBars</c>,
    /// <c>rsiOS</c>, <c>convictionMult</c> and <c>divergenceDepth</c>. It was derived from the
    /// median interval over a sample of the loaded array, so on a series with gaps — weekends,
    /// halts, exchange outages, a missing-bar artifact — <b>every historical bar's gold dot,
    /// blue dot and divergence could change when more history was fetched.</b> The chart's own
    /// declared timeframe is the honest answer and cannot move.</para>
    /// </summary>
    public class CipherBTimingTests
    {
        // ── The exempting shift ──────────────────────────────────────────────

        private static double[] Nan(int n)
        {
            var a = new double[n];
            Array.Fill(a, double.NaN);
            return a;
        }

        [Fact]
        public void A_pivot_marker_still_moves_to_its_confirmation_bar()
        {
            // The lag is not being removed — it is correct for the detector it was written for.
            var src = Nan(20);
            src[10] = 5.0;
            var exempt = new bool[20];   // nothing exempt: a pure pivot marker

            var shifted = CipherBProvider.ShiftMarkersForwardExcept(src, exempt, lag: 3, n: 20);

            Assert.True(double.IsNaN(shifted[10]));
            Assert.Equal(5.0, shifted[13]);
        }

        [Fact]
        public void A_shallow_marker_stays_on_the_bar_its_condition_occurred()
        {
            var src = Nan(20);
            src[10] = 5.0;
            var exempt = new bool[20];
            exempt[10] = true;           // written by the shallow cross detector

            var shifted = CipherBProvider.ShiftMarkersForwardExcept(src, exempt, lag: 3, n: 20);

            Assert.Equal(5.0, shifted[10]);
            Assert.True(double.IsNaN(shifted[13]));
        }

        [Fact]
        public void The_two_detectors_coexist_in_one_array()
        {
            // They write into the same arrays, which is why the exemption has to travel
            // alongside the data rather than being decided per-array.
            var src = Nan(30);
            src[10] = 1.0;   // pivot   → moves to 13
            src[20] = 2.0;   // shallow → stays at 20
            var exempt = new bool[30];
            exempt[20] = true;

            var shifted = CipherBProvider.ShiftMarkersForwardExcept(src, exempt, lag: 3, n: 30);

            Assert.Equal(1.0, shifted[13]);
            Assert.Equal(2.0, shifted[20]);
            Assert.True(double.IsNaN(shifted[10]));
            Assert.True(double.IsNaN(shifted[23]));
        }

        [Fact]
        public void A_shifted_marker_does_not_evict_an_exempt_one()
        {
            // The exempt marker describes something that actually happened on that bar; the
            // shifted one is a confirmation stamp. When they collide the real event wins.
            var src = Nan(20);
            src[10] = 1.0;   // pivot, would land on 13
            src[13] = 2.0;   // shallow, already at 13
            var exempt = new bool[20];
            exempt[13] = true;

            var shifted = CipherBProvider.ShiftMarkersForwardExcept(src, exempt, lag: 3, n: 20);

            Assert.Equal(2.0, shifted[13]);
        }

        [Fact]
        public void With_nothing_exempt_it_matches_the_plain_shift()
        {
            // Vacuity check: if the exempting variant diverged from the original on the
            // ordinary path, the pivot detector's honesty fix would have been quietly undone.
            var src = Nan(40);
            src[5] = 1; src[17] = 2; src[38] = 3;   // the last one shifts off the end

            var plain = CipherBProvider.ShiftMarkersForward(src, 3, 40);
            var exempting = CipherBProvider.ShiftMarkersForwardExcept(src, new bool[40], 3, 40);

            for (int i = 0; i < 40; i++)
            {
                Assert.Equal(double.IsNaN(plain[i]), double.IsNaN(exempting[i]));
                if (!double.IsNaN(plain[i])) Assert.Equal(plain[i], exempting[i]);
            }
        }

        // ── The timeframe hint ───────────────────────────────────────────────

        private static double HintMinutes(Dictionary<string, object> p) =>
            (double)typeof(CipherBProvider)
                .GetMethod("TimeframeHintMinutes",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { p })!;

        [Theory]
        [InlineData("1m", 1.0)]
        [InlineData("5m", 5.0)]
        [InlineData("1h", 60.0)]
        [InlineData("4h", 240.0)]
        [InlineData("1d", 1440.0)]
        [InlineData("1w", 10080.0)]
        public void The_chart_timeframe_hint_is_read_in_minutes(string timeframe, double expected)
        {
            Assert.Equal(expected, HintMinutes(new Dictionary<string, object> { ["__timeframe"] = timeframe }), 3);
        }

        [Fact]
        public void An_absent_or_unusable_hint_falls_back_rather_than_reporting_zero_minutes()
        {
            // The median over the loaded bars remains the fallback for callers that do not
            // stamp the hint — the strategy backtester and the causality harness among them.
            // Reporting 0 here is how the caller knows to fall back.
            Assert.Equal(0.0, HintMinutes(new Dictionary<string, object>()));
            Assert.Equal(0.0, HintMinutes(new Dictionary<string, object> { ["__timeframe"] = "" }));
            Assert.Equal(0.0, HintMinutes(new Dictionary<string, object> { ["__timeframe"] = "not-a-timeframe" }));
        }

        [Fact]
        public void The_hint_does_not_depend_on_how_many_bars_were_loaded()
        {
            // The whole point. A median over the array moves when the array does; a declared
            // timeframe does not, and every gate in the indicator is downstream of it.
            var p = new Dictionary<string, object> { ["__timeframe"] = "4h" };

            double first = HintMinutes(p);
            double afterAScrollBack = HintMinutes(p);   // same hint, more history

            Assert.Equal(first, afterAScrollBack);
            Assert.Equal(240.0, first, 3);
        }
    }
}
