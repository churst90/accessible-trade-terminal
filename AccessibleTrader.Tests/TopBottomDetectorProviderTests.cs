using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    public class TopBottomDetectorProviderTests
    {
        private static readonly TopBottomDetectorProvider Provider = new();

        private static Dictionary<string, object> DefaultParams() => new()
        {
            { "LookbackWindow",     100  },
            { "PivotLookback",        5  },
            { "DistributionDecay",   30  },
            { "ConfirmThreshold",   0.6  },
            { "AtrPeriod",           14  },
            { "RsiPeriod",           14  },
        };

        private static Dictionary<string, double[]> Run(Ohlcv[] bars, Dictionary<string, object>? p = null)
        {
            var rd  = new Dictionary<string, double[]>();
            var buf = new IndicatorResultBuffer(rd, bars.Length);
            Provider.Calculate(TopBottomDetectorProvider.Code, bars.AsSpan(), p ?? DefaultParams(), buf);
            return rd;
        }

        // ── Metadata ──────────────────────────────────────────────────────────────

        [Fact]
        public void Metadata_HasExpectedComponents()
        {
            var meta = Provider.GetIndicators()[0];
            Assert.Equal(TopBottomDetectorProvider.Code, meta.Code);
            Assert.Equal(4, meta.Components.Count);
            Assert.Contains(meta.Components, c => c.Name == TopBottomDetectorProvider.CompCapitulation);
            Assert.Contains(meta.Components, c => c.Name == TopBottomDetectorProvider.CompDistribution);
            Assert.Contains(meta.Components, c => c.Name == TopBottomDetectorProvider.CompTop);
            Assert.Contains(meta.Components, c => c.Name == TopBottomDetectorProvider.CompBottom);
        }

        [Fact]
        public void Metadata_HasAllSevenParameters()
        {
            var meta = Provider.GetIndicators()[0];
            Assert.Equal(7, meta.Parameters.Count);
            Assert.Contains(meta.Parameters, p => p.Name == "TimeframeAdaptive");
        }

        // ── Bar-interval detection ────────────────────────────────────────────────

        [Fact]
        public void DetectBarIntervalMinutes_HourlyBars_Returns60()
        {
            var bars = Enumerable.Range(0, 5)
                .Select(i => new Ohlcv(new DateTime(2024, 1, 1).AddHours(i), 100, 101, 99, 100, 1000))
                .ToArray();
            Assert.Equal(60.0, TopBottomDetectorProvider.DetectBarIntervalMinutes(bars));
        }

        [Fact]
        public void DetectBarIntervalMinutes_DailyBars_Returns1440()
        {
            var bars = Enumerable.Range(0, 5)
                .Select(i => new Ohlcv(new DateTime(2024, 1, 1).AddDays(i), 100, 101, 99, 100, 1000))
                .ToArray();
            Assert.Equal(1440.0, TopBottomDetectorProvider.DetectBarIntervalMinutes(bars));
        }

        [Fact]
        public void DetectBarIntervalMinutes_WithGap_TakesMedian()
        {
            // 1h bars but one gap of 6h between bars 3 and 4. Median of (1,1,1,6,1,1,1,1,1,1,1) is 1h.
            var bars = new List<Ohlcv>();
            var t = new DateTime(2024, 1, 1);
            for (int i = 0; i < 4; i++) { bars.Add(new Ohlcv(t, 100, 101, 99, 100, 1000)); t = t.AddHours(1); }
            t = t.AddHours(5); // 6h gap
            for (int i = 0; i < 8; i++) { bars.Add(new Ohlcv(t, 100, 101, 99, 100, 1000)); t = t.AddHours(1); }
            Assert.Equal(60.0, TopBottomDetectorProvider.DetectBarIntervalMinutes(bars.ToArray()));
        }

        // ── Adaptive scaling preserves opt-out backwards compat ───────────────────

        [Fact]
        public void TimeframeAdaptive_DefaultOff_DoesNotChangeBehavior()
        {
            // Same engineered capitulation as the existing test; with adaptive flag absent
            // (or set to 0), behavior must match the original implementation. Compares the
            // capitulation curve produced with default params vs. params with adaptive=0.
            var bars = BuildEngineeredCapitulationBars();

            var withDefault = Run(bars, DefaultParams());
            var explicitOff = new Dictionary<string, object>(DefaultParams())
            {
                ["TimeframeAdaptive"] = 0
            };
            var withOff = Run(bars, explicitOff);

            for (int i = 0; i < bars.Length; i++)
            {
                double a = withDefault[TopBottomDetectorProvider.CompCapitulation][i];
                double b = withOff[TopBottomDetectorProvider.CompCapitulation][i];
                Assert.True((double.IsNaN(a) && double.IsNaN(b)) || a == b,
                    $"Bar {i}: default={a}, explicit-off={b}");
            }
        }

        [Fact]
        public void TimeframeAdaptive_OnDailyBars_IsNoOp()
        {
            // Empirical research: default 100/30/0.6 produces +0.654R on BTC 1d, the
            // best result in the v22 investigation. Adaptation must NOT touch 1d/4h
            // gates — only weekly+. Verify by running the same daily series with and
            // without adaptive and checking the capitulation curve is bit-identical.
            var bars = new Ohlcv[200];
            var anchor = new DateTime(2024, 1, 1);
            var rng = new Random(13);
            for (int i = 0; i < 200; i++)
            {
                double c = 100.0 + Math.Sin(i * 0.05) * 5.0 + (rng.NextDouble() - 0.5) * 0.5;
                bars[i] = new Ohlcv(anchor.AddDays(i), c, c + 0.6, c - 0.6, c, 1000.0);
            }

            var off = Run(bars, DefaultParams());
            var on  = Run(bars, new Dictionary<string, object>(DefaultParams())
            {
                ["TimeframeAdaptive"] = 1
            });

            for (int i = 0; i < 200; i++)
            {
                double a = off[TopBottomDetectorProvider.CompCapitulation][i];
                double b = on[TopBottomDetectorProvider.CompCapitulation][i];
                Assert.True((double.IsNaN(a) && double.IsNaN(b)) || a == b,
                    $"1d bar {i}: adaptation must be no-op (off={a}, on={b}).");
            }
        }

        [Fact]
        public void TimeframeAdaptive_OnWeeklyBars_RelaxesGates()
        {
            // 200 weekly bars (~4 years). With adaptive off, 100-bar lookback eats the
            // first 100 weeks before the gate satisfies; with adaptive on, weekly+
            // scales lookback to ~38 bars so the gate satisfies sooner.
            var bars = new Ohlcv[200];
            var anchor = new DateTime(2020, 1, 1);
            var rng = new Random(17);
            for (int i = 0; i < 200; i++)
            {
                double c = 100.0 + Math.Sin(i * 0.07) * 8.0 + (rng.NextDouble() - 0.5) * 1.0;
                bars[i] = new Ohlcv(anchor.AddDays(i * 7), c, c + 1.0, c - 1.0, c, 1000.0);
            }

            var off = Run(bars, DefaultParams());
            var on  = Run(bars, new Dictionary<string, object>(DefaultParams())
            {
                ["TimeframeAdaptive"] = 1
            });

            int firstNonNaNOff = -1, firstNonNaNOn = -1;
            for (int i = 0; i < 200; i++)
            {
                if (firstNonNaNOff == -1 && !double.IsNaN(off[TopBottomDetectorProvider.CompCapitulation][i]))
                    firstNonNaNOff = i;
                if (firstNonNaNOn == -1 && !double.IsNaN(on[TopBottomDetectorProvider.CompCapitulation][i]))
                    firstNonNaNOn = i;
            }

            Assert.True(firstNonNaNOn >= 0,  $"Adaptive should produce weekly values; got {firstNonNaNOn}.");
            Assert.True(firstNonNaNOff >= 0, $"Default should also produce weekly values; got {firstNonNaNOff}.");
            Assert.True(firstNonNaNOn < firstNonNaNOff,
                $"Adaptive should warm up sooner on weekly ({firstNonNaNOn} vs {firstNonNaNOff}).");
        }

        private static Ohlcv[] BuildEngineeredCapitulationBars()
        {
            var bars = new Ohlcv[200];
            var anchor = new DateTime(2024, 1, 1);
            var rng = new Random(7);

            for (int i = 0; i < 175; i++)
            {
                double noise = (rng.NextDouble() - 0.5) * 0.5;
                double price = i < 160 ? 100.0 + noise : 100.0 - 20.0 * ((i - 160) / 14.0) + noise;
                bars[i] = new Ohlcv(anchor.AddHours(i), price, price + 0.4, price - 0.4, price, 1000.0);
            }
            bars[175] = new Ohlcv(anchor.AddHours(175), 80.5, 81.0, 65.0, 76.0, 15000.0);
            for (int i = 176; i < 200; i++)
            {
                double t = (i - 175) / 24.0;
                double price = 76.0 + 10.0 * t + (rng.NextDouble() - 0.5) * 0.5;
                bars[i] = new Ohlcv(anchor.AddHours(i), price, price + 0.4, price - 0.4, price, 1000.0);
            }
            return bars;
        }

        [Fact]
        public void StabilityWindow_IsLookbackPlusPivotPlusFive()
        {
            var w = Provider.GetStabilityWindow(TopBottomDetectorProvider.Code, DefaultParams());
            Assert.Equal(110, w);
        }

        // ── Edge: too-short data ──────────────────────────────────────────────────

        [Fact]
        public void TooFewBars_AllNaN()
        {
            var bars = Enumerable.Range(0, 50)
                .Select(i => new Ohlcv(new DateTime(2024, 1, 1).AddHours(i), 100, 101, 99, 100, 1000))
                .ToArray();
            var r = Run(bars);
            Assert.All(r[TopBottomDetectorProvider.CompCapitulation], v => Assert.True(double.IsNaN(v)));
            Assert.All(r[TopBottomDetectorProvider.CompDistribution], v => Assert.True(double.IsNaN(v)));
            Assert.All(r[TopBottomDetectorProvider.CompTop],          v => Assert.True(double.IsNaN(v)));
            Assert.All(r[TopBottomDetectorProvider.CompBottom],       v => Assert.True(double.IsNaN(v)));
        }

        // ── Flat market: no signals ───────────────────────────────────────────────

        [Fact]
        public void FlatMarket_NoConfirmations()
        {
            var rng = new Random(42);
            var bars = new Ohlcv[200];
            var anchor = new DateTime(2024, 1, 1);
            for (int i = 0; i < 200; i++)
            {
                double noise = (rng.NextDouble() - 0.5) * 0.4;
                double c = 100.0 + noise;
                double h = c + 0.3;
                double l = c - 0.3;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, h, l, c, 1000.0);
            }

            var r = Run(bars);

            Assert.All(r[TopBottomDetectorProvider.CompTop],    v => Assert.True(double.IsNaN(v)));
            Assert.All(r[TopBottomDetectorProvider.CompBottom], v => Assert.True(double.IsNaN(v)));

            // Confidences should mostly stay below the 0.6 confirm threshold.
            int topConfNonNaN = r[TopBottomDetectorProvider.CompDistribution].Count(v => !double.IsNaN(v));
            int topConfHigh   = r[TopBottomDetectorProvider.CompDistribution].Count(v => !double.IsNaN(v) && v >= 0.6);
            Assert.True(topConfNonNaN > 0);
            Assert.True(topConfHigh < topConfNonNaN / 4,
                $"Distribution should rarely exceed confirm in flat noise: {topConfHigh}/{topConfNonNaN}");
        }

        // ── Engineered capitulation event ─────────────────────────────────────────

        [Fact]
        public void Capitulation_FiresOnEngineeredBottom()
        {
            // 200 bars: warmup, then a 20-bar downtrend, then ONE huge capitulation bar,
            // then recovery.
            var bars = new Ohlcv[200];
            var anchor = new DateTime(2024, 1, 1);
            var rng = new Random(7);

            for (int i = 0; i < 175; i++)
            {
                // Bars 0-159 noisy at $100. Bars 160-174 trend down from $100 → $80.
                double noise = (rng.NextDouble() - 0.5) * 0.5;
                double price;
                if (i < 160)
                {
                    price = 100.0 + noise;
                }
                else
                {
                    double t = (i - 160) / 14.0;
                    price = 100.0 - 20.0 * t + noise;
                }
                double h = price + 0.4;
                double l = price - 0.4;
                bars[i] = new Ohlcv(anchor.AddHours(i), price, h, l, price, 1000.0);
            }

            // Bar 175 — capitulation: massive lower wick, big volume, low pierces band.
            bars[175] = new Ohlcv(
                anchor.AddHours(175),
                Open:   80.5,
                High:   81.0,
                Low:    65.0,   // long lower wick — 15 below open
                Close:  76.0,   // close in upper portion of bar
                Volume: 15000.0 // 15× normal volume
            );

            // Bars 176-199 — recovery
            for (int i = 176; i < 200; i++)
            {
                double t = (i - 175) / 24.0;
                double price = 76.0 + 10.0 * t + (rng.NextDouble() - 0.5) * 0.5;
                bars[i] = new Ohlcv(anchor.AddHours(i), price, price + 0.4, price - 0.4, price, 1000.0);
            }

            var r = Run(bars);

            double cap175 = r[TopBottomDetectorProvider.CompCapitulation][175];
            Assert.False(double.IsNaN(cap175));
            Assert.True(cap175 >= 0.5,
                $"Capitulation at bar 175 should be high; got {cap175:F3}");

            // Bottom Confirmed should fire at or near the capitulation bar.
            var bot = r[TopBottomDetectorProvider.CompBottom];
            int firedAround = 0;
            for (int i = 173; i <= 178 && i < bot.Length; i++)
                if (!double.IsNaN(bot[i])) firedAround++;
            Assert.True(firedAround > 0,
                "Bottom Confirmed should fire on or adjacent to the engineered capitulation bar.");
        }

        // ── Engineered distribution top ───────────────────────────────────────────

        [Fact]
        public void Distribution_AccumulatesOnEngineeredTop()
        {
            // Build a series with multiple bearish-divergence highs + drying volume +
            // sideways at top so the distribution accumulator builds confidence.
            var bars = new Ohlcv[260];
            var anchor = new DateTime(2024, 1, 1);
            var rng = new Random(9);

            // Bars 0-119: warm-up oscillation around $100, normal volume.
            for (int i = 0; i < 120; i++)
            {
                double c = 100.0 + Math.Sin(i * 0.2) * 1.5 + (rng.NextDouble() - 0.5) * 0.3;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, c + 0.5, c - 0.5, c, 1000.0);
            }

            // Bars 120-139: strong rally from $100 → $115, big volume.
            for (int i = 120; i < 140; i++)
            {
                double t = (i - 120) / 19.0;
                double c = 100.0 + 15.0 * t + (rng.NextDouble() - 0.5) * 0.3;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, c + 0.5, c - 0.5, c, 3000.0);
            }

            // Bars 140-154: pullback to $111, lower volume — sets up swing high at ~134.
            for (int i = 140; i < 155; i++)
            {
                double t = (i - 140) / 14.0;
                double c = 115.0 - 4.0 * t + (rng.NextDouble() - 0.5) * 0.3;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, c + 0.5, c - 0.5, c, 1500.0);
            }

            // Bars 155-179: second rally to $116 (HH), but volume DRYING UP (1200 → 800).
            // RSI peaks lower than the first rally because momentum slowing.
            for (int i = 155; i < 180; i++)
            {
                double t = (i - 155) / 24.0;
                double c = 111.0 + 5.0 * t + (rng.NextDouble() - 0.5) * 0.4;
                double vol = 1200.0 - 400.0 * t;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, c + 0.4, c - 0.4, c, vol);
            }

            // Bars 180-199: sideways at top $115±0.8, low volume 700, range tightening.
            for (int i = 180; i < 200; i++)
            {
                double c = 115.0 + Math.Sin(i * 0.4) * 0.6 + (rng.NextDouble() - 0.5) * 0.2;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, c + 0.3, c - 0.3, c, 700.0);
            }

            // Bars 200-209: upthrust sequence — pop above prior $116 high then close back below.
            for (int i = 200; i < 210; i++)
            {
                double c = 115.5 + (rng.NextDouble() - 0.5) * 0.3;
                double h = i == 205 ? 117.5 : c + 0.4;
                double cl = i == 205 ? 115.0 : c;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, h, c - 0.3, cl, 800.0);
            }

            // Bars 210-259: roll over downward.
            for (int i = 210; i < 260; i++)
            {
                double t = (i - 210) / 49.0;
                double c = 115.0 - 10.0 * t + (rng.NextDouble() - 0.5) * 0.3;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, c + 0.4, c - 0.4, c, 1000.0);
            }

            var r = Run(bars);

            var dist = r[TopBottomDetectorProvider.CompDistribution];

            // Distribution should reach a clearly elevated reading somewhere in the
            // 180-220 region (sideways-at-top + upthrust + drying volume).
            double maxInTopZone = 0;
            for (int i = 175; i < 220; i++)
                if (!double.IsNaN(dist[i]) && dist[i] > maxInTopZone) maxInTopZone = dist[i];
            Assert.True(maxInTopZone >= 0.5,
                $"Distribution should reach ≥0.5 in the engineered top zone; max was {maxInTopZone:F3}");

            // And during the warm-up flat zone it should stay low — distribution evidence
            // shouldn't accumulate without divergence/upthrust evidence.
            double maxInFlat = 0;
            for (int i = 100; i < 120; i++)
                if (!double.IsNaN(dist[i]) && dist[i] > maxInFlat) maxInFlat = dist[i];
            Assert.True(maxInFlat < 0.3,
                $"Distribution should stay low during flat warmup; max was {maxInFlat:F3}");
        }

        // ── Asymmetry: bottoms are events, tops are processes ─────────────────────

        [Fact]
        public void Asymmetry_CapitulationIsSpiky_DistributionIsSmooth()
        {
            // On the same engineered series we use for the top test, the capitulation
            // confidence should be NOISY (single-bar events) while distribution should
            // be SMOOTH (slowly-accumulating). Quantify by measuring bar-to-bar absolute
            // delta of each component over the same range.
            var bars = new Ohlcv[260];
            var anchor = new DateTime(2024, 1, 1);
            var rng = new Random(11);
            for (int i = 0; i < 260; i++)
            {
                double c = 100.0 + Math.Sin(i * 0.05) * 5.0 + (rng.NextDouble() - 0.5) * 0.5;
                bars[i] = new Ohlcv(anchor.AddHours(i), c, c + 0.6, c - 0.6,
                                    c + (rng.NextDouble() - 0.5) * 0.3,
                                    1000.0 + (rng.NextDouble() - 0.5) * 200);
            }

            var r = Run(bars);
            var cap  = r[TopBottomDetectorProvider.CompCapitulation];
            var dist = r[TopBottomDetectorProvider.CompDistribution];

            double capJitter = 0, distJitter = 0;
            int counted = 0;
            for (int i = 110; i < 260; i++)
            {
                if (double.IsNaN(cap[i]) || double.IsNaN(cap[i - 1])
                 || double.IsNaN(dist[i]) || double.IsNaN(dist[i - 1])) continue;
                capJitter  += Math.Abs(cap[i]  - cap[i - 1]);
                distJitter += Math.Abs(dist[i] - dist[i - 1]);
                counted++;
            }
            Assert.True(counted > 50);
            // Distribution accumulator with decay should be much smoother than the
            // single-bar capitulation score.
            Assert.True(distJitter < capJitter,
                $"Expected distribution jitter ({distJitter:F2}) < capitulation jitter ({capJitter:F2})");
        }
        // ── Prepend stability: which population is the residue in? ────────────

        /// <summary>
        /// <c>TOP_BOTTOM_DETECTOR.Distribution Confidence</c> disagrees with itself when older
        /// bars are prepended, and it is exempted from
        /// <c>IndicatorCausalityTests.NotStableWhenHistoryIsPrepended</c>. That list holds two
        /// populations and the whole value of it is knowing which one an entry is in: a filter
        /// still forgetting its seed CONVERGES, and something pinned to array index 0 does not —
        /// it re-cuts, and the disagreement stays the same size forever.
        ///
        /// <para>
        /// The TODO filed this one as "not investigated", sitting among the index-0 defects. It
        /// is not one. The accumulator is <c>dist = dist * 0.5^(1/30) + evidence</c>, seeded at
        /// zero in BOTH runs, so the seed is not the source at all — the source is the Wilder
        /// ATR and RSI feeding <c>evidence</c>, whose own seed residue this multiplies by the
        /// accumulator's steady-state gain of 1/(1 - 0.5^(1/30)) ≈ 43. That is why it is still
        /// above the guard's 1e-6 at bar 700 when every other converging filter is under 5e-7.
        /// </para>
        ///
        /// <para>
        /// This test is the measurement that settles it: prepend 91 bars and watch the
        /// disagreement across the series. It falls from ~4e-2 near the short run's start to
        /// ~1e-7 a thousand bars later — five orders of magnitude of decay. An index anchor
        /// cannot do that, so the exemption is inherent and there is nothing to fix.
        /// </para>
        /// </summary>
        [Fact]
        public void DistributionConfidence_ResidueConverges_ItIsNotAnchoredToArrayIndexZero()
        {
            var provider = new TopBottomDetectorProvider();
            var meta = provider.GetIndicators()[0];
            var pars = new Dictionary<string, object>();
            foreach (var q in meta.Parameters) pars[q.Name] = q.DefaultValue;

            const int drop = 91;
            var full  = IndicatorCausalityTests.Bars(0, 1400);
            var trimmed = full.Skip(drop).ToList();

            var a = RunOn(provider, meta.Code, pars, full);
            var b = RunOn(provider, meta.Code, pars, trimmed);

            double WorstRelativeGap(int fromShortIndex, int count)
            {
                double worst = 0;
                for (int j = fromShortIndex; j < Math.Min(fromShortIndex + count, trimmed.Count); j++)
                {
                    double x = a[TopBottomDetectorProvider.CompDistribution][j + drop];
                    double y = b[TopBottomDetectorProvider.CompDistribution][j];
                    if (double.IsNaN(x) || double.IsNaN(y)) continue;
                    worst = Math.Max(worst, Math.Abs(x - y) / Math.Max(1e-9, Math.Abs(x)));
                }
                return worst;
            }

            double early = WorstRelativeGap(300, 200);
            double late  = WorstRelativeGap(1100, 200);

            // Vacuity check first: if the early window already agreed there would be no residue
            // to classify and the decay assertion below would pass for the wrong reason.
            Assert.True(early > 1e-3, $"no measurable residue near the array start ({early:E2}) — nothing was tested");
            Assert.True(late < 1e-5, $"residue was still {late:E2} a thousand bars in — that is not convergence");
            Assert.True(late < early / 1000.0, $"residue barely decayed: {early:E2} -> {late:E2}");
        }

        private static Dictionary<string, double[]> RunOn(TopBottomDetectorProvider provider, string code,
            Dictionary<string, object> pars, List<Ohlcv> bars)
        {
            var rd = new Dictionary<string, double[]>();
            var buf = new IndicatorResultBuffer(rd, bars.Count);
            provider.Calculate(code, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars), pars, buf);
            return rd;
        }
    }
}
