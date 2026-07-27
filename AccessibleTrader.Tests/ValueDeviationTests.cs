using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coverage for <see cref="ValueDeviationAnalyzer"/> and <see cref="ValueDeviationProvider"/>.
    ///
    /// The invariants worth defending: the profile must be CAUSAL (a POC computed from future
    /// bars would make every tier a lie), tiers must deepen with distance, a collapsed value area
    /// must not manufacture an extreme reading, and the marks must land on reversal bars only.
    /// </summary>
    public class ValueDeviationTests
    {
        private static readonly DateTime Start = new(2020, 1, 1);

        private static List<Ohlcv> Flat(int count, double price, double vol = 1000)
        {
            var bars = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
                bars.Add(new Ohlcv(Start.AddDays(i), price, price + 0.5, price - 0.5, price, vol));
            return bars;
        }

        // ── Causality ─────────────────────────────────────────────────────────

        [Fact]
        public void ProfileIsCausal_FutureBarsCannotChangeAnEarlierPoc()
        {
            // Build a series, take the reference, then append a violent move and take it again.
            // Every value at an index that existed before must be unchanged.
            var bars = Flat(400, 100);
            var analyzer = new ValueDeviationAnalyzer();
            var before = analyzer.Reference(bars, 120).Poc;

            var extended = new List<Ohlcv>(bars);
            for (int i = 0; i < 120; i++)
                extended.Add(new Ohlcv(Start.AddDays(400 + i), 500, 505, 495, 500, 100000));

            var after = analyzer.Reference(extended, 120).Poc;

            for (int i = 0; i < bars.Count; i++)
            {
                if (double.IsNaN(before[i]) && double.IsNaN(after[i])) continue;
                Assert.Equal(before[i], after[i], 6);
            }
        }

        [Fact]
        public void BarsBeforeTheWindowFills_HaveNoReading()
        {
            var bars = Flat(300, 100);
            var devs = new ValueDeviationAnalyzer().Analyze(bars, 120, 5, 2.0);

            for (int i = 0; i < 120; i++)
                Assert.Equal(0, devs[i].Tier);
        }

        // ── Tier behaviour ────────────────────────────────────────────────────

        [Fact]
        public void DeeperDeviationProducesADeeperTier()
        {
            // 300 bars of two-sided business around 100 to give the profile a real value area,
            // then a drop that steps progressively further below it.
            var bars = new List<Ohlcv>();
            var rng = new Random(7);
            for (int i = 0; i < 300; i++)
            {
                double p = 100 + (rng.NextDouble() - 0.5) * 8;
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 1, p - 1, p, 1000));
            }
            int drop = bars.Count;
            foreach (double p in new[] { 96.0, 92.0, 86.0, 78.0 })
                for (int k = 0; k < 6; k++)
                    bars.Add(new Ohlcv(Start.AddDays(bars.Count), p, p + 0.5, p - 0.5, p, 50));

            var devs = new ValueDeviationAnalyzer().Analyze(bars, 250, 5, 2.0);

            var tiers = new List<int>();
            for (int leg = 0; leg < 4; leg++)
            {
                int idx = drop + leg * 6 + 5;
                if (idx < devs.Length && devs[idx].Tier > 0) tiers.Add(devs[idx].Tier);
            }

            Assert.True(tiers.Count >= 3, "expected readings on most legs");
            for (int i = 1; i < tiers.Count; i++)
                Assert.True(tiers[i] >= tiers[i - 1],
                    $"tier must not shrink as price falls further: {string.Join(",", tiers)}");
        }

        [Fact]
        public void PriceInsideValue_ReportsTierZero()
        {
            var bars = Flat(400, 100);
            var devs = new ValueDeviationAnalyzer().Analyze(bars, 120, 5, 2.0);
            Assert.Equal(0, devs[^1].Tier);
        }

        [Fact]
        public void BelowValueIsFlaggedAsSuch()
        {
            var bars = new List<Ohlcv>();
            var rng = new Random(11);
            for (int i = 0; i < 300; i++)
            {
                double p = 100 + (rng.NextDouble() - 0.5) * 8;
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 1, p - 1, p, 1000));
            }
            for (int i = 0; i < 20; i++)
                bars.Add(new Ohlcv(Start.AddDays(bars.Count), 80, 80.5, 79.5, 80, 50));

            var devs = new ValueDeviationAnalyzer().Analyze(bars, 250, 5, 2.0);
            var last = devs[^1];
            Assert.True(last.Tier > 0);
            Assert.True(last.BelowValue);
            Assert.True(last.DeviationVa < 0);
        }

        [Fact]
        public void ZeroVolumeSeries_ProducesNoReadingRatherThanGarbage()
        {
            var bars = Flat(400, 100, vol: 0);
            var devs = new ValueDeviationAnalyzer().Analyze(bars, 120, 5, 2.0);
            Assert.All(devs, d => Assert.Equal(0, d.Tier));
        }

        [Fact]
        public void TierNeverExceedsTheConfiguredCount()
        {
            var bars = new List<Ohlcv>();
            var rng = new Random(3);
            for (int i = 0; i < 300; i++)
            {
                double p = 100 + (rng.NextDouble() - 0.5) * 8;
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 1, p - 1, p, 1000));
            }
            for (int i = 0; i < 20; i++)
                bars.Add(new Ohlcv(Start.AddDays(bars.Count), 40, 40.5, 39.5, 40, 10));

            var devs = new ValueDeviationAnalyzer().Analyze(bars, 250, 5, 2.0);
            Assert.All(devs, d => Assert.InRange(d.Tier, 0, 5));
        }

        // ── Provider surface ──────────────────────────────────────────────────

        private sealed class Buf : AccessibleTrader.Sdk.Interfaces.IIndicatorResultBuffer
        {
            public readonly Dictionary<string, double[]> Data = new();
            private readonly int _n;
            public Buf(int n) => _n = n;
            public Span<double> GetComponentSpan(string name)
            {
                if (!Data.TryGetValue(name, out var a)) { a = new double[_n]; Data[name] = a; }
                return a;
            }
            public void SetValue(string name, int i, double v) => GetComponentSpan(name)[i] = v;
            public void WriteZoneBands(string code, List<ZoneBandConfig> z) { }
            public IReadOnlyList<ZoneBandConfig> ReadZoneBands(string code) => Array.Empty<ZoneBandConfig>();
        }

        private static (Buf Buffer, List<Ohlcv> Bars) RunProvider(bool requireMomentum = false)
        {
            var bars = new List<Ohlcv>();
            var rng = new Random(23);
            for (int i = 0; i < 400; i++)
            {
                double p = 100 + (rng.NextDouble() - 0.5) * 10;
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 1.2, p - 1.2, p, 1000));
            }
            // A sharp flush well below value, ending in a clean bullish reversal bar.
            for (int i = 0; i < 14; i++)
                bars.Add(new Ohlcv(Start.AddDays(bars.Count), 84 - i * 0.6, 84.4 - i * 0.6, 83.2 - i * 0.6, 83.4 - i * 0.6, 400));
            var prev = bars[^1];
            bars.Add(new Ohlcv(Start.AddDays(bars.Count), prev.Close, prev.Close + 2.5, prev.Low - 1.5, prev.Close + 2.0, 900));

            var buf = new Buf(bars.Count);
            new ValueDeviationProvider().Calculate(ValueDeviationProvider.Code, bars.ToArray(),
                new Dictionary<string, object>
                {
                    ["Window"] = 300, ["Tiers"] = 5, ["MaxTierVa"] = 2.0,
                    ["RequireMomentumTurn"] = requireMomentum ? 1 : 0,
                }, buf);
            return (buf, bars);
        }

        [Fact]
        public void Provider_PrintsASupportZoneOnTheReversalBarBelowValue()
        {
            var (buf, bars) = RunProvider();
            int last = bars.Count - 1;

            double shallow = buf.Data[ValueDeviationProvider.CompSupportShallow][last];
            double mid = buf.Data[ValueDeviationProvider.CompSupportMid][last];
            double deep = buf.Data[ValueDeviationProvider.CompSupportDeep][last];

            Assert.True(!double.IsNaN(shallow) || !double.IsNaN(mid) || !double.IsNaN(deep),
                "expected a support-zone mark on the reversal bar");
        }

        [Fact]
        public void Provider_SupportZoneValueIsTheBarLowItMarks()
        {
            var (buf, bars) = RunProvider();
            for (int i = 0; i < bars.Count; i++)
            {
                foreach (var key in new[] { ValueDeviationProvider.CompSupportShallow,
                                            ValueDeviationProvider.CompSupportMid,
                                            ValueDeviationProvider.CompSupportDeep })
                {
                    double v = buf.Data[key][i];
                    // The VALUE is the support price itself (the bar's low). Where the shape is
                    // drawn is MarkerAnchor's job, so the value stays a real, quotable price.
                    if (!double.IsNaN(v)) Assert.Equal(bars[i].Low, v, 6);
                }
            }
        }

        [Fact]
        public void Provider_NeverMarksABarThatIsNotAReversal()
        {
            var (buf, bars) = RunProvider();
            for (int i = 1; i < bars.Count; i++)
            {
                bool marked = new[] { ValueDeviationProvider.CompSupportShallow,
                                      ValueDeviationProvider.CompSupportMid,
                                      ValueDeviationProvider.CompSupportDeep }
                    .Any(k => !double.IsNaN(buf.Data[k][i]));
                if (!marked) continue;

                double range = bars[i].High - bars[i].Low;
                Assert.True(bars[i].Low < bars[i - 1].Low, $"bar {i} did not undercut the prior low");
                Assert.True(bars[i].Close > bars[i].Open, $"bar {i} did not close up");
                Assert.True((bars[i].Close - bars[i].Low) / range > 0.5, $"bar {i} did not close strong");
            }
        }

        [Fact]
        public void Provider_ExposesThePocAndValueBounds()
        {
            var (buf, bars) = RunProvider();
            Assert.False(double.IsNaN(buf.Data[ValueDeviationProvider.CompPoc][^1]));
            Assert.True(buf.Data[ValueDeviationProvider.CompValueHigh][^1]
                        > buf.Data[ValueDeviationProvider.CompValueLow][^1]);
        }

        [Fact]
        public void Provider_DetailFactRefusesToPromiseAnything()
        {
            var (buf, bars) = RunProvider();
            string fact = new ValueDeviationProvider().GetDetailFact(ValueDeviationProvider.Code,
                bars.ToArray(), buf.Data, bars.Count - 1, new Dictionary<string, object>());

            Assert.Contains("value", fact, StringComparison.OrdinalIgnoreCase);
            // It reports what was measured historically, never a forecast.
            Assert.DoesNotContain("will ", fact, StringComparison.OrdinalIgnoreCase);
        }

        // ── Regression: the indicator was silent on every real chart ──────────

        /// <summary>
        /// Trending series with realistic two-sided noise — the shape a live chart actually has,
        /// as opposed to the synthetic fixtures above.
        /// </summary>
        private static List<Ohlcv> Realistic(int count, int seed = 99)
        {
            var rng = new Random(seed);
            var bars = new List<Ohlcv>(count);
            double price = 100;
            for (int i = 0; i < count; i++)
            {
                price *= 1 + (rng.NextDouble() - 0.48) * 0.05;
                if (price < 1) price = 1;
                double range = price * (0.01 + rng.NextDouble() * 0.03);
                double open = price + (rng.NextDouble() - 0.5) * range;
                double close = price + (rng.NextDouble() - 0.5) * range;
                bars.Add(new Ohlcv(Start.AddDays(i), open,
                    Math.Max(open, close) + rng.NextDouble() * range * 0.5,
                    Math.Min(open, close) - rng.NextDouble() * range * 0.5,
                    close, 1000 + rng.Next(0, 4000)));
            }
            return bars;
        }

        private static Buf CalcDefaults(List<Ohlcv> bars)
        {
            var buf = new Buf(bars.Count);
            new ValueDeviationProvider().Calculate(ValueDeviationProvider.Code, bars.ToArray(),
                new Dictionary<string, object>(), buf);
            return buf;
        }

        [Theory]
        [InlineData(200)]
        [InlineData(300)]
        [InlineData(600)]
        public void Provider_ProducesOutputAtBarCountsARealChartActuallyLoads(int barCount)
        {
            // The first release defaulted the profile window to 480 while a fresh chart fetches
            // about 200 bars, so every component came back entirely NaN and read "no data".
            var buf = CalcDefaults(Realistic(barCount));

            int poc = buf.Data[ValueDeviationProvider.CompPoc].Count(v => !double.IsNaN(v));
            Assert.True(poc > 0, $"no POC values at {barCount} bars — the window is larger than the data");
        }

        [Fact]
        public void Provider_ActuallyPrintsMarksWithDefaultSettings()
        {
            // The real defect this guards: WaveTrend chained three EMAs, the second seeded from a
            // NaN warmup region, and the NaN propagated through the whole oscillator. The momentum
            // filter then rejected EVERY bar and the indicator printed nothing at all — while the
            // POC and value lines still looked perfectly healthy, so nothing pointed at the cause.
            var buf = CalcDefaults(Realistic(800));

            int marks = new[]
            {
                ValueDeviationProvider.CompSupportShallow, ValueDeviationProvider.CompSupportMid,
                ValueDeviationProvider.CompSupportDeep, ValueDeviationProvider.CompResistShallow,
                ValueDeviationProvider.CompResistMid, ValueDeviationProvider.CompResistDeep,
            }.Sum(k => buf.Data[k].Count(v => !double.IsNaN(v)));

            Assert.True(marks > 0, "indicator printed no marks at all on 800 realistic bars");
        }

        [Fact]
        public void Provider_MomentumFilterRemovesSomeMarksButNotAllOfThem()
        {
            // Pins the filter as a filter. All-or-nothing in either direction is the bug signature:
            // nothing removed means it is not wired in, everything removed is the NaN failure.
            var bars = Realistic(800);

            var on = new Buf(bars.Count);
            new ValueDeviationProvider().Calculate(ValueDeviationProvider.Code, bars.ToArray(),
                new Dictionary<string, object> { ["RequireMomentumTurn"] = 1 }, on);

            var off = new Buf(bars.Count);
            new ValueDeviationProvider().Calculate(ValueDeviationProvider.Code, bars.ToArray(),
                new Dictionary<string, object> { ["RequireMomentumTurn"] = 0 }, off);

            int Count(Buf b) => new[]
            {
                ValueDeviationProvider.CompSupportShallow, ValueDeviationProvider.CompSupportMid,
                ValueDeviationProvider.CompSupportDeep, ValueDeviationProvider.CompResistShallow,
                ValueDeviationProvider.CompResistMid, ValueDeviationProvider.CompResistDeep,
            }.Sum(k => b.Data[k].Count(v => !double.IsNaN(v)));

            int withFilter = Count(on), withoutFilter = Count(off);
            Assert.True(withFilter > 0, "momentum filter removed every mark");
            Assert.True(withFilter < withoutFilter, "momentum filter removed nothing — is it wired in?");
        }

        [Fact]
        public void Provider_OversizedWindowIsCappedRatherThanSilencingEverything()
        {
            // Asking for far more history than exists must degrade, not go quiet.
            var bars = Realistic(300);
            var buf = new Buf(bars.Count);
            new ValueDeviationProvider().Calculate(ValueDeviationProvider.Code, bars.ToArray(),
                new Dictionary<string, object> { ["Window"] = 2000 }, buf);

            Assert.True(buf.Data[ValueDeviationProvider.CompPoc].Count(v => !double.IsNaN(v)) > 0);
        }

        [Fact]
        public void Provider_ResistanceSpeechDescribesAZoneNotATrade()
        {
            string? speech = new ValueDeviationProvider().GetComponentSpeech(
                "Resistance tier 4-5", 100, new Ohlcv(Start, 1, 1, 1, 1, 1),
                new Dictionary<string, double[]>(), 0);

            Assert.NotNull(speech);
            Assert.Contains("resistance zone", speech, StringComparison.OrdinalIgnoreCase);
            // It describes a zone. It must never read as a trade instruction in either direction.
            Assert.DoesNotContain("short", speech!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sell", speech!, StringComparison.OrdinalIgnoreCase);
        }
        // ── Marker density ────────────────────────────────────────────────────

        private static int MarkCount(Buf b) => new[]
        {
            ValueDeviationProvider.CompSupportShallow, ValueDeviationProvider.CompSupportMid,
            ValueDeviationProvider.CompSupportDeep, ValueDeviationProvider.CompResistShallow,
            ValueDeviationProvider.CompResistMid, ValueDeviationProvider.CompResistDeep,
        }.Sum(k => b.Data[k].Count(v => !double.IsNaN(v)));

        private static Buf RunWithMinTier(List<Ohlcv> bars, int minTier)
        {
            var buf = new Buf(bars.Count);
            new ValueDeviationProvider().Calculate(ValueDeviationProvider.Code, bars.ToArray(),
                new Dictionary<string, object> { ["MinTier"] = minTier, ["RequireMomentumTurn"] = 0 }, buf);
            return buf;
        }

        [Fact]
        public void MinTier_RaisingItStrictlyReducesTheNumberOfMarks()
        {
            // The reason this parameter exists: on a 200-bar weekly chart the tier-1 marks — a
            // reversal barely outside value, which is closer to noise than to a zone — made the
            // price pane a continuous band of glyphs.
            var bars = Realistic(800);

            int t1 = MarkCount(RunWithMinTier(bars, 1));
            int t2 = MarkCount(RunWithMinTier(bars, 2));
            int t4 = MarkCount(RunWithMinTier(bars, 4));

            Assert.True(t1 > 0, "no marks at all at tier 1 — the filter is not the thing being tested");
            Assert.True(t2 < t1, "raising the minimum tier removed nothing — is it wired in?");
            Assert.True(t4 < t2, "tier 4 must be stricter than tier 2");
        }

        [Fact]
        public void MinTier_HidesTheGlyphButNeverTheInformation()
        {
            // The Deviation Tier component and the spoken detail still report every tier, so
            // navigating to a bar or asking for its detail still says "tier 1 below value". This
            // is a density control, not a data filter — hiding the reading as well would make the
            // chart quieter by making it lie.
            var bars = Realistic(800);

            var loose = RunWithMinTier(bars, 1);
            var strict = RunWithMinTier(bars, 5);

            var looseTiers = loose.Data[ValueDeviationProvider.CompTier];
            var strictTiers = strict.Data[ValueDeviationProvider.CompTier];

            Assert.Equal(looseTiers.Length, strictTiers.Length);
            for (int i = 0; i < looseTiers.Length; i++)
            {
                if (double.IsNaN(looseTiers[i])) Assert.True(double.IsNaN(strictTiers[i]));
                else Assert.Equal(looseTiers[i], strictTiers[i]);
            }

            // And the reference lines are equally untouched.
            Assert.Equal(loose.Data[ValueDeviationProvider.CompPoc].Count(v => !double.IsNaN(v)),
                         strict.Data[ValueDeviationProvider.CompPoc].Count(v => !double.IsNaN(v)));
        }

        [Fact]
        public void MinTier_DefaultsToTwoSoAFreshChartIsNotAWallOfTierOneMarks()
        {
            var bars = Realistic(800);

            var explicitTwo = RunWithMinTier(bars, 2);

            var defaulted = new Buf(bars.Count);
            new ValueDeviationProvider().Calculate(ValueDeviationProvider.Code, bars.ToArray(),
                new Dictionary<string, object> { ["RequireMomentumTurn"] = 0 }, defaulted);

            Assert.Equal(MarkCount(explicitTwo), MarkCount(defaulted));
            Assert.True(MarkCount(defaulted) < MarkCount(RunWithMinTier(bars, 1)),
                "the default must actually thin the chart, or it is not solving the problem it exists for");
        }

        [Fact]
        public void MinTier_IsClampedSoAnAbsurdValueCannotSilenceOrFloodTheChart()
        {
            var bars = Realistic(800);

            // 0 and negatives clamp to 1 (show everything); anything above 5 clamps to 5.
            Assert.Equal(MarkCount(RunWithMinTier(bars, 1)), MarkCount(RunWithMinTier(bars, 0)));
            Assert.Equal(MarkCount(RunWithMinTier(bars, 1)), MarkCount(RunWithMinTier(bars, -3)));
            Assert.Equal(MarkCount(RunWithMinTier(bars, 5)), MarkCount(RunWithMinTier(bars, 99)));
        }

        [Fact]
        public void MinTier_SurvivesEveryMarkBeingFilteredWithoutThrowing()
        {
            // A flat series has no meaningful profile peak and no deep tiers; the strictest
            // setting must return a clean empty result rather than fall over.
            var buf = RunWithMinTier(Flat(400, 100), 5);

            Assert.Equal(0, MarkCount(buf));
        }
    }
}