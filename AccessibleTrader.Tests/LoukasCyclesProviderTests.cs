using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for <see cref="LoukasCyclesProvider"/>. Focus areas:
    ///   • Metadata shape (component count, pane, parameters)
    ///   • Stability window math
    ///   • Swing-low detection honours SwingLookback symmetry
    ///   • DCL promotion gated on DcMinBars (early swings are NOT DCLs)
    ///   • DC day count resets on DCL confirmation
    ///   • DC In Window / DC Overdue bands fire at the right counts
    ///   • IC DC counter rolls over at IcDcCount and emits an ICL
    ///   • Four-Year Cycle components populate using halving anchors
    ///   • Four-Year components are NaN before the earliest halving
    /// </summary>
    public class LoukasCyclesProviderTests
    {
        private static readonly LoukasCyclesProvider Provider = new();

        private static Dictionary<string, object> DefaultParams() => new()
        {
            { "DcMinBars",           35   },
            { "DcMaxBars",           90   },
            { "SwingLookback",       10   },
            { "IcDcCount",           3    },
            { "EnableFourYearCycle", true },
        };

        // ── Builders ──────────────────────────────────────────────────────────

        /// <summary>
        /// Build a sawtooth price series with each successive cycle low slightly lower
        /// than the previous (a structured downtrend with DC structure, mimicking a
        /// bear-market sequence of DCLs). Each cycle's trough is the unique lowest low
        /// in its DC, which is what the canonical Loukas DCL filter requires.
        /// </summary>
        private static Ohlcv[] BuildSawtoothFromHalving(int count, int period, DateTime? start = null)
        {
            var anchor = start ?? new DateTime(2020, 5, 11, 0, 0, 0, DateTimeKind.Utc);
            var bars = new Ohlcv[count];
            for (int i = 0; i < count; i++)
            {
                int    cycleIdx = i / period;
                int    phase    = i % period;
                double half     = period / 2.0;
                double dist     = Math.Abs(phase - half);
                // Baseline drifts down 5 units per cycle so each successive low is
                // lower than every prior bar — guarantees the "lowest in DC" filter
                // passes for the trough of each new cycle.
                double baseline = 200.0 - 5.0 * cycleIdx;
                double close    = baseline + dist;
                double low      = close - 0.25;
                double high     = close + 1.0;
                bars[i] = new Ohlcv(anchor.AddDays(i), close - 0.1, high, low, close, 1000);
            }
            return bars;
        }

        /// <summary>
        /// Build a series whose price rises monotonically from bar 0 — no local swing
        /// lows anywhere. Used to verify that the DC day count simply climbs without
        /// any DCL resets.
        /// </summary>
        private static Ohlcv[] BuildMonotonicRising(int count, DateTime? start = null)
        {
            var anchor = start ?? new DateTime(2020, 5, 11, 0, 0, 0, DateTimeKind.Utc);
            var bars = new Ohlcv[count];
            for (int i = 0; i < count; i++)
            {
                double c = 100.0 + i;
                bars[i] = new Ohlcv(anchor.AddDays(i), c - 0.5, c + 1.0, c - 0.25, c, 1000);
            }
            return bars;
        }

        private static Dictionary<string, double[]> Run(Ohlcv[] bars, Dictionary<string, object>? p = null)
        {
            var rd  = new Dictionary<string, double[]>();
            var buf = new IndicatorResultBuffer(rd, bars.Length);
            Provider.Calculate("LOUKAS_CYCLES", bars.AsSpan(), p ?? DefaultParams(), buf);
            return rd;
        }

        // ── 1. Metadata ───────────────────────────────────────────────────────

        [Fact]
        public void Metadata_HasExpectedComponents()
        {
            var meta = Provider.GetIndicators()[0];
            Assert.Equal("LOUKAS_CYCLES", meta.Code);
            Assert.Equal(8, meta.Components.Count);
            Assert.Contains(meta.Components, c => c.Name == LoukasCyclesProvider.CompDcDayCount);
            Assert.Contains(meta.Components, c => c.Name == LoukasCyclesProvider.CompDclConfirmed);
            Assert.Contains(meta.Components, c => c.Name == LoukasCyclesProvider.CompIclConfirmed);
            Assert.Contains(meta.Components, c => c.Name == LoukasCyclesProvider.CompFyPhase);
        }

        [Fact]
        public void Metadata_HasAllFiveParameters()
        {
            var meta = Provider.GetIndicators()[0];
            Assert.Equal(5, meta.Parameters.Count);
        }

        // ── 2. Stability window ───────────────────────────────────────────────

        [Fact]
        public void StabilityWindow_MatchesDcMaxPlusSwingTail()
        {
            var window = Provider.GetStabilityWindow("LOUKAS_CYCLES", DefaultParams());
            // DcMaxBars + SwingLookback*2 = 90 + 20 = 110
            Assert.Equal(110, window);
        }

        // ── 3. DC day count with no swings ────────────────────────────────────

        [Fact]
        public void MonotonicRising_DcDayCountClimbsWithoutReset()
        {
            var bars    = BuildMonotonicRising(100);
            var results = Run(bars);

            var dayCount = results[LoukasCyclesProvider.CompDcDayCount];
            // On a monotonic uptrend there are NO swing lows → no DCL fires → count
            // equals bar index throughout.
            for (int i = 0; i < bars.Length; i++)
            {
                Assert.Equal(i, (int)dayCount[i]);
            }

            // No DCL / ICL confirmations anywhere.
            Assert.All(results[LoukasCyclesProvider.CompDclConfirmed], v => Assert.True(double.IsNaN(v)));
            Assert.All(results[LoukasCyclesProvider.CompIclConfirmed], v => Assert.True(double.IsNaN(v)));
        }

        // ── 4. Day count enters the "in window" band ──────────────────────────

        [Fact]
        public void MonotonicRising_InWindowFiresBetweenMinAndMaxBars()
        {
            var bars    = BuildMonotonicRising(120);
            var results = Run(bars);

            var inWin    = results[LoukasCyclesProvider.CompDcInWindow];
            var overdue  = results[LoukasCyclesProvider.CompDcOverdue];

            // Bars 0..34 are early → NaN
            for (int i = 0; i < 35; i++)
                Assert.True(double.IsNaN(inWin[i]), $"bar {i} should be NaN (early)");

            // Bars 35..90 are in window → non-NaN (DcMaxBars default raised to 90)
            for (int i = 35; i <= 90; i++)
                Assert.False(double.IsNaN(inWin[i]), $"bar {i} should be in window");

            // Bars 91+ are overdue → inWin NaN, overdue non-NaN
            for (int i = 91; i < 120; i++)
            {
                Assert.True(double.IsNaN(inWin[i]),    $"bar {i} should not be in window");
                Assert.False(double.IsNaN(overdue[i]), $"bar {i} should be overdue");
            }
        }

        // ── 5. DCL promotion respects DcMinBars ───────────────────────────────

        [Fact]
        public void SwingLowInsideMinBarsWindow_IsNotPromotedToDcl()
        {
            // Period=20 sawtooth truncated to 34 bars → only the swing low at bar 10
            // exists, and its day-count (10) is well below DcMinBars=35. Must not be
            // promoted. A longer series would eventually produce a swing past day 35
            // and become a DCL — that case is covered by the next test.
            var bars    = BuildSawtoothFromHalving(34, 20);
            var results = Run(bars);

            var dcl = results[LoukasCyclesProvider.CompDclConfirmed];
            // No DCL confirmations — every swing fell inside the early window.
            Assert.All(dcl, v => Assert.True(double.IsNaN(v)));
        }

        [Fact]
        public void SwingLowPastMinBars_IsPromotedToDcl()
        {
            // Period=40 sawtooth → swing lows every 40 bars. First swing is well inside
            // the 35..60 DCL timing window and should be promoted.
            var bars    = BuildSawtoothFromHalving(300, 40);
            var results = Run(bars);

            var dcl      = results[LoukasCyclesProvider.CompDclConfirmed];
            var dclCount = dcl.Count(v => !double.IsNaN(v));

            // Across 300 bars with a 40-bar cycle there should be several DCL events.
            Assert.True(dclCount >= 4, $"expected ≥4 DCLs, got {dclCount}");
        }

        // ── 6. DC day count resets on DCL ─────────────────────────────────────

        [Fact]
        public void DayCount_ResetsToZeroOnDclConfirmation()
        {
            var bars    = BuildSawtoothFromHalving(300, 40);
            var results = Run(bars);

            var dcl      = results[LoukasCyclesProvider.CompDclConfirmed];
            var dayCount = results[LoukasCyclesProvider.CompDcDayCount];

            // Find the first DCL confirmation and verify the next bar's count is < the
            // previous bar's count (the reset happened). The DCL bar itself is the
            // CONFIRMATION bar; the swing low was SwingLookback bars earlier, so the
            // reset value equals SwingLookback (not 0) — that's correct and documented
            // in the provider.
            for (int i = 1; i < bars.Length - 1; i++)
            {
                if (!double.IsNaN(dcl[i]))
                {
                    Assert.True(dayCount[i] < dayCount[i - 1],
                        $"day count did not reset at DCL bar {i}: prev={dayCount[i-1]}, cur={dayCount[i]}");
                    return;
                }
            }
            Assert.Fail("expected at least one DCL confirmation in the series");
        }

        // ── 7. ICL rollover ───────────────────────────────────────────────────

        [Fact]
        public void IclConfirmed_FiresAfterIcDcCountDcls()
        {
            // 500 bars × 40-period sawtooth → plenty of DCLs, should see at least one ICL.
            var bars    = BuildSawtoothFromHalving(500, 40);
            var results = Run(bars);

            var icl = results[LoukasCyclesProvider.CompIclConfirmed];
            var iclCount = icl.Count(v => !double.IsNaN(v));

            Assert.True(iclCount >= 1, $"expected ≥1 ICL, got {iclCount}");
        }

        /// <summary>
        /// Bull-market regression test. Each successive cycle low is HIGHER than the
        /// previous one (an uptrend with cycle structure, like BTC 2020-2024). The
        /// canonical Loukas DCL filter uses a SLIDING window, so this case must still
        /// produce DCLs. A "lowest since last DCL" filter would lock out every bar
        /// after the first DCL — exactly the bug we hit on real BTC data.
        /// </summary>
        [Fact]
        public void BullMarketSawtooth_StillProducesDcls()
        {
            // Period=40 sawtooth, baseline rises 5 units per cycle (uptrend with DC
            // structure). Successive cycle lows are progressively HIGHER, opposite of
            // BuildSawtoothFromHalving's bear shape.
            var anchor = new DateTime(2020, 5, 11, 0, 0, 0, DateTimeKind.Utc);
            int count = 500, period = 40;
            var bars = new Ohlcv[count];
            for (int i = 0; i < count; i++)
            {
                int    cycleIdx = i / period;
                int    phase    = i % period;
                double half     = period / 2.0;
                double dist     = Math.Abs(phase - half);
                double baseline = 100.0 + 5.0 * cycleIdx;
                double close    = baseline + dist;
                double low      = close - 0.25;
                double high     = close + 1.0;
                bars[i] = new Ohlcv(anchor.AddDays(i), close - 0.1, high, low, close, 1000);
            }

            var rd  = new Dictionary<string, double[]>();
            var buf = new IndicatorResultBuffer(rd, count);
            Provider.Calculate("LOUKAS_CYCLES", bars.AsSpan(), DefaultParams(), buf);

            var dcl      = rd[LoukasCyclesProvider.CompDclConfirmed];
            var dclCount = dcl.Count(v => !double.IsNaN(v));
            Assert.True(dclCount >= 4,
                $"Bull-market sawtooth should still fire DCLs at each cycle trough. Got {dclCount}.");

            // Day count should also reset somewhere in the middle of the series — i.e.
            // not climb monotonically all the way to 500. Find the max day count and
            // verify it's well below the series length.
            var dayCount = rd[LoukasCyclesProvider.CompDcDayCount];
            int maxDay = (int)dayCount.Max();
            Assert.True(maxDay < 100,
                $"Day count never reset — max was {maxDay}, expected well below series length.");
        }

        // ── 8. Four-Year Cycle components ─────────────────────────────────────

        [Fact]
        public void FyComponents_PopulateAfterHalvingAnchor()
        {
            // Start from the 2020 halving — every bar should have a non-NaN FY count.
            var bars    = BuildMonotonicRising(100, new DateTime(2020, 5, 11, 0, 0, 0, DateTimeKind.Utc));
            var results = Run(bars);

            var fyDay   = results[LoukasCyclesProvider.CompFyDayCount];
            var fyPhase = results[LoukasCyclesProvider.CompFyPhase];

            Assert.All(fyDay,   v => Assert.False(double.IsNaN(v)));
            Assert.All(fyPhase, v => Assert.False(double.IsNaN(v)));

            // Bar 0 is exactly on the halving → day 0, phase 0 (Accumulation)
            Assert.Equal(0.0, fyDay[0],   precision: 3);
            Assert.Equal(0.0, fyPhase[0], precision: 3);

            // Bar 99 → day 99, still in Accumulation (< 180)
            Assert.Equal(99.0, fyDay[99],   precision: 3);
            Assert.Equal(0.0,  fyPhase[99], precision: 3);
        }

        [Fact]
        public void FyComponents_AreNanBeforeEarliestHalving()
        {
            // Start well before the 2012 halving — FY has no anchor and must be NaN.
            var bars    = BuildMonotonicRising(20, new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var results = Run(bars);

            Assert.All(results[LoukasCyclesProvider.CompFyDayCount], v => Assert.True(double.IsNaN(v)));
            Assert.All(results[LoukasCyclesProvider.CompFyPhase],    v => Assert.True(double.IsNaN(v)));
        }

        [Fact]
        public void FyComponents_DisabledByParameter()
        {
            var bars = BuildMonotonicRising(50);
            var p    = DefaultParams();
            p["EnableFourYearCycle"] = false;

            var results = Run(bars, p);
            Assert.All(results[LoukasCyclesProvider.CompFyDayCount], v => Assert.True(double.IsNaN(v)));
            Assert.All(results[LoukasCyclesProvider.CompFyPhase],    v => Assert.True(double.IsNaN(v)));
        }

        // ── 9. Degenerate inputs ──────────────────────────────────────────────

        [Fact]
        public void EmptyData_DoesNotThrow()
        {
            var rd  = new Dictionary<string, double[]>();
            var buf = new IndicatorResultBuffer(rd, 0);
            Provider.Calculate("LOUKAS_CYCLES", Array.Empty<Ohlcv>().AsSpan(), DefaultParams(), buf);
            // No exception is the assertion.
        }

        [Fact]
        public void WrongCode_IsIgnored()
        {
            var bars = BuildMonotonicRising(10);
            var rd   = new Dictionary<string, double[]>();
            var buf  = new IndicatorResultBuffer(rd, bars.Length);
            Provider.Calculate("NOT_LOUKAS", bars.AsSpan(), DefaultParams(), buf);
            // Buffer should contain nothing new.
            Assert.Empty(rd);
        }

        // ── 10. GetDetailFact ─────────────────────────────────────────────────

        [Fact]
        public void GetDetailFact_DescribesPhase()
        {
            var bars    = BuildMonotonicRising(120);
            var results = Run(bars);

            // Bar 40 should be "in window"
            var fact = Provider.GetDetailFact("LOUKAS_CYCLES", bars.AsSpan(), results, 40, DefaultParams());
            Assert.Contains("in window", fact, StringComparison.OrdinalIgnoreCase);

            // Bar 110 should be "overdue" (past DcMaxBars=90)
            fact = Provider.GetDetailFact("LOUKAS_CYCLES", bars.AsSpan(), results, 110, DefaultParams());
            Assert.Contains("overdue", fact, StringComparison.OrdinalIgnoreCase);
        }
    }
}
